using System;
using System.Collections.Generic;
using Convai.Application.Services.Transcript;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Presenters;
using Convai.Runtime.Presentation.Strategies;
using Convai.Runtime.Utilities;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Controller for managing transcript UI instances using a Strategy pattern.
    ///     Routes transcript events through mode-specific strategies to registered UI implementations.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Architecture:</b> This controller uses the Strategy pattern to handle different
    ///         transcript presentation modes (Chat, Subtitle, Q&amp;A). Each mode has a dedicated
    ///         strategy that handles aggregation, completion, and state management.
    ///     </para>
    ///     <para>
    ///         This controller:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Maintains a registry of ITranscriptUI implementations</description>
    ///             </item>
    ///             <item>
    ///                 <description>Subscribes to EventHub for transcript events via TranscriptPresenter</description>
    ///             </item>
    ///             <item>
    ///                 <description>Routes events through the active ITranscriptPresentationStrategy</description>
    ///             </item>
    ///             <item>
    ///                 <description>Routes strategy output to registered UIs</description>
    ///             </item>
    ///             <item>
    ///                 <description>Routes messages to ITranscriptListener implementations for simple transcript access</description>
    ///             </item>
    ///             <item>
    ///                 <description>Handles UI lifecycle (registration, initialization, disposal)</description>
    ///             </item>
    ///             <item>
    ///                 <description>Manages mode-based UI activation (Chat, Subtitle, QuestionAnswer)</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public class TranscriptUIController : IDisposable
    {
        private readonly IEventHub _eventHub;
        private readonly ILogger _logger;

        private readonly TranscriptPresenter _presenter;
        private readonly Dictionary<string, ITranscriptUI> _registeredUIs = new();

        private readonly Dictionary<TranscriptUIMode, ITranscriptPresentationStrategy> _strategies;

        private readonly List<ITranscriptListener> _transcriptListeners = new();

        private SubscriptionToken? _characterSpeechStateToken;
        private SubscriptionToken? _characterTurnCompletedToken;
        private TranscriptUIMode _currentMode = TranscriptUIMode.Chat;
        private bool _disposed;

        /// <summary>
        ///     Creates a new TranscriptUIController with the specified dependencies.
        /// </summary>
        /// <param name="eventHub">The event hub for subscribing to transcript events.</param>
        /// <param name="formatter">Optional custom transcript formatter.</param>
        /// <param name="filter">Optional custom transcript filter.</param>
        /// <param name="logger">Optional logger for strategy debugging.</param>
        public TranscriptUIController(
            IEventHub eventHub,
            ITranscriptFormatter formatter = null,
            ITranscriptFilter filter = null,
            ILogger logger = null)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _logger = logger;

            _presenter = new TranscriptPresenter(
                eventHub,
                formatter ?? new DefaultTranscriptFormatter(),
                filter ?? new DefaultTranscriptFilter(),
                null,
                logger);

            _presenter.TranscriptReceived += OnTranscriptReceived;

            _strategies = new Dictionary<TranscriptUIMode, ITranscriptPresentationStrategy>
            {
                { TranscriptUIMode.Chat, new ChatPresentationStrategy(logger) },
                { TranscriptUIMode.Subtitle, new SubtitlePresentationStrategy(logger) },
                { TranscriptUIMode.QuestionAnswer, new QAPresentationStrategy(logger) }
            };

            ActiveStrategy = _strategies[_currentMode];
            SubscribeToStrategy(ActiveStrategy);

            _characterSpeechStateToken =
                _eventHub.Subscribe<CharacterSpeechStateChanged>(OnCharacterSpeechStateChanged);
            _characterTurnCompletedToken = _eventHub.Subscribe<CharacterTurnCompleted>(OnCharacterTurnCompleted);

            IsEnabled = true;

            ConvaiLogger.Debug("[TranscriptUIController] Initialized with Strategy pattern", LogCategory.UI);
        }

        /// <summary>
        ///     Gets the currently active transcript UI.
        /// </summary>
        public ITranscriptUI ActiveUI { get; private set; }

        /// <summary>
        ///     Gets the currently active presentation strategy.
        /// </summary>
        public ITranscriptPresentationStrategy ActiveStrategy { get; private set; }

        /// <summary>
        ///     Gets whether transcript routing is currently enabled.
        /// </summary>
        public bool IsEnabled { get; private set; } = true;

        /// <summary>
        ///     Gets or sets the current transcript UI mode.
        /// </summary>
        public TranscriptUIMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (_currentMode != value)
                {
                    TranscriptUIMode previousMode = _currentMode;
                    _currentMode = value;
                    UpdateActiveStrategy();
                    UpdateActiveUI();
                    ConvaiLogger.Debug($"[TranscriptUIController] Mode changed from {previousMode} to {_currentMode}",
                        LogCategory.UI);
                }
            }
        }

        /// <summary>
        ///     Disposes the controller and all registered UIs.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_presenter != null)
            {
                _presenter.TranscriptReceived -= OnTranscriptReceived;
                _presenter.Dispose();
            }

            if (ActiveStrategy != null) UnsubscribeFromStrategy(ActiveStrategy);

            foreach (ITranscriptPresentationStrategy strategy in _strategies.Values) strategy.Dispose();
            _strategies.Clear();

            if (_eventHub != null)
            {
                if (_characterSpeechStateToken.HasValue)
                {
                    _eventHub.Unsubscribe(_characterSpeechStateToken.Value);
                    _characterSpeechStateToken = null;
                }

                if (_characterTurnCompletedToken.HasValue)
                {
                    _eventHub.Unsubscribe(_characterTurnCompletedToken.Value);
                    _characterTurnCompletedToken = null;
                }
            }

            _transcriptListeners.Clear();
            _registeredUIs.Clear();
            ActiveUI = null;

            ConvaiLogger.Debug("[TranscriptUIController] Disposed", LogCategory.UI);
        }

        /// <summary>
        ///     Event raised when the active UI changes.
        /// </summary>
        public event Action<ITranscriptUI> ActiveUIChanged;

        /// <summary>
        ///     Registers a transcript UI implementation.
        /// </summary>
        /// <param name="ui">The UI to register.</param>
        public void Register(ITranscriptUI ui)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));

            if (_registeredUIs.ContainsKey(ui.Identifier))
            {
                ConvaiLogger.Warning(
                    $"[TranscriptUIController] UI with identifier '{ui.Identifier}' already registered. Replacing.",
                    LogCategory.UI);
                Unregister(ui.Identifier);
            }

            _registeredUIs[ui.Identifier] = ui;
            UpdateActiveUI();
        }

        /// <summary>
        ///     Unregisters a transcript UI by identifier.
        /// </summary>
        /// <param name="identifier">The identifier of the UI to unregister.</param>
        public void Unregister(string identifier)
        {
            if (_registeredUIs.TryGetValue(identifier, out ITranscriptUI ui))
            {
                _registeredUIs.Remove(identifier);

                if (ActiveUI == ui)
                {
                    ActiveUI = null;
                    UpdateActiveUI();
                }
            }
        }

        /// <summary>
        ///     Gets a registered UI by identifier.
        /// </summary>
        public bool TryGetUI(string identifier, out ITranscriptUI ui) => _registeredUIs.TryGetValue(identifier, out ui);

        /// <summary>
        ///     Clears all messages in all registered UIs and resets the active strategy.
        /// </summary>
        public void ClearAll()
        {
            ActiveStrategy?.ClearAll();

            foreach (ITranscriptUI ui in _registeredUIs.Values) ui.ClearAll();
        }

        /// <summary>
        ///     Enables or disables transcript routing and UI activation.
        ///     When disabled, all registered UIs are cleared and hidden.
        /// </summary>
        /// <param name="enabled">True to enable routing; false to disable.</param>
        public void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;

            IsEnabled = enabled;

            if (!enabled)
            {
                ActiveStrategy?.ClearAll();

                foreach (ITranscriptUI ui in _registeredUIs.Values)
                {
                    try
                    {
                        ui.ClearAll();
                        if (ui.IsActive) ui.SetActive(false);
                    }
                    catch (Exception ex)
                    {
                        ConvaiLogger.Error(
                            $"[TranscriptUIController] Error disabling UI '{ui.Identifier}': {ex.Message}",
                            LogCategory.UI);
                    }
                }

                string previousId = ActiveUI?.Identifier ?? "none";
                ActiveUI = null;
                ConvaiLogger.Debug(
                    $"[TranscriptUIController] Transcript routing disabled; active UI changed from '{previousId}' to 'none'",
                    LogCategory.UI);
                ActiveUIChanged?.Invoke(ActiveUI);
            }
            else
            {
                ConvaiLogger.Debug("[TranscriptUIController] Transcript routing enabled", LogCategory.UI);
                UpdateActiveUI();
            }
        }

        #region ITranscriptListener Support

        /// <summary>
        ///     Registers a transcript listener for simple transcript access.
        /// </summary>
        public void RegisterListener(ITranscriptListener listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));

            if (!_transcriptListeners.Contains(listener))
            {
                _transcriptListeners.Add(listener);
                ConvaiLogger.Debug(
                    $"[TranscriptUIController] Registered ITranscriptListener: {listener.GetType().Name}",
                    LogCategory.UI);
            }
        }

        /// <summary>
        ///     Unregisters a transcript listener.
        /// </summary>
        public void UnregisterListener(ITranscriptListener listener)
        {
            if (listener != null && _transcriptListeners.Remove(listener))
            {
                ConvaiLogger.Debug(
                    $"[TranscriptUIController] Unregistered ITranscriptListener: {listener.GetType().Name}",
                    LogCategory.UI);
            }
        }

        /// <summary>
        ///     Discovers and registers all ITranscriptListener components in the active scene.
        /// </summary>
        public void DiscoverListenersInScene()
        {
            IReadOnlyList<ITranscriptListener> sceneListeners =
                InterfaceComponentQuery.FindObjects<ITranscriptListener>();

            int discoveredCount = 0;
            for (int i = 0; i < sceneListeners.Count; i++)
            {
                RegisterListener(sceneListeners[i]);
                discoveredCount++;
            }

            ConvaiLogger.Debug($"[TranscriptUIController] Discovered {discoveredCount} ITranscriptListener(s) in scene",
                LogCategory.UI);
        }

        /// <summary>
        ///     Gets the number of registered transcript listeners.
        /// </summary>
        public int TranscriptListenerCount => _transcriptListeners.Count;

        #endregion

        #region Strategy Management

        private void UpdateActiveStrategy()
        {
            if (_strategies.TryGetValue(_currentMode, out ITranscriptPresentationStrategy newStrategy))
            {
                if (newStrategy != ActiveStrategy)
                {
                    if (ActiveStrategy != null)
                    {
                        ActiveStrategy.ClearAll();
                        UnsubscribeFromStrategy(ActiveStrategy);
                    }

                    ActiveStrategy = newStrategy;
                    SubscribeToStrategy(ActiveStrategy);

                    ConvaiLogger.Debug($"[TranscriptUIController] Active strategy changed to {_currentMode}",
                        LogCategory.UI);
                }
            }
        }

        private void SubscribeToStrategy(ITranscriptPresentationStrategy strategy)
        {
            strategy.OnMessageUpdated += OnStrategyMessageUpdated;
            strategy.OnMessageCompleted += OnStrategyMessageCompleted;
        }

        private void UnsubscribeFromStrategy(ITranscriptPresentationStrategy strategy)
        {
            strategy.OnMessageUpdated -= OnStrategyMessageUpdated;
            strategy.OnMessageCompleted -= OnStrategyMessageCompleted;
        }

        /// <summary>
        ///     Handles message updates from the active strategy.
        ///     Routes aggregated/processed messages to active UIs.
        /// </summary>
        private void OnStrategyMessageUpdated(TranscriptViewModel viewModel)
        {
            if (_disposed || !IsEnabled) return;

            foreach (ITranscriptUI ui in _registeredUIs.Values)
            {
                if (ui.IsActive)
                {
                    try
                    {
                        ui.DisplayMessage(viewModel);
                    }
                    catch (Exception ex)
                    {
                        ConvaiLogger.Error(
                            $"[TranscriptUIController] Error routing to UI '{ui.Identifier}': {ex.Message}",
                            LogCategory.UI);
                    }
                }
            }
        }

        /// <summary>
        ///     Handles message completion from the active strategy.
        /// </summary>
        private void OnStrategyMessageCompleted(string messageId)
        {
            if (_disposed || !IsEnabled) return;

            foreach (ITranscriptUI ui in _registeredUIs.Values)
            {
                if (ui.IsActive)
                {
                    try
                    {
                        ui.CompleteMessage(messageId);
                    }
                    catch (Exception ex)
                    {
                        ConvaiLogger.Error(
                            $"[TranscriptUIController] Error completing message for UI '{ui.Identifier}': {ex.Message}",
                            LogCategory.UI);
                    }
                }
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        ///     Handles transcript events from the presenter.
        ///     Routes to strategy AND directly to ITranscriptListeners.
        /// </summary>
        private void OnTranscriptReceived(TranscriptViewModel viewModel)
        {
            if (_disposed || !IsEnabled) return;

            foreach (ITranscriptListener listener in _transcriptListeners)
            {
                try
                {
                    if (ShouldReceive(listener, viewModel))
                    {
                        if (viewModel.Speaker == TranscriptSpeaker.Character)
                        {
                            listener.OnCharacterTranscript(
                                viewModel.SpeakerId,
                                viewModel.DisplayName,
                                viewModel.Text,
                                viewModel.IsFinal);
                        }
                        else if (viewModel.Speaker == TranscriptSpeaker.Player)
                        {
                            listener.OnPlayerTranscript(viewModel.Text, viewModel.IsFinal);

                            if (listener is IMultiUserTranscriptListener multiUserListener && viewModel.HasSpeakerInfo)
                            {
                                multiUserListener.OnPlayerTranscriptWithSpeaker(
                                    viewModel.SpeakerId,
                                    viewModel.DisplayName,
                                    viewModel.ParticipantId,
                                    viewModel.Text,
                                    viewModel.IsFinal);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Error(
                        $"[TranscriptUIController] Error routing to ITranscriptListener '{listener.GetType().Name}': {ex.Message}",
                        LogCategory.UI);
                }
            }

            ActiveStrategy?.HandleMessage(viewModel);
        }

        private bool ShouldReceive(ITranscriptListener listener, TranscriptViewModel viewModel)
        {
            string filter = listener.FilterCharacterId;
            return filter == null || filter == viewModel.SpeakerId;
        }

        private void OnCharacterSpeechStateChanged(CharacterSpeechStateChanged speechState)
        {
            if (_disposed || !IsEnabled) return;

            if (speechState.IsSpeaking)
            {
                ConvaiLogger.Debug(
                    "[TranscriptUIController] Character started speaking - completing any pending player turn (fallback)",
                    LogCategory.UI);
                ActiveStrategy?.CompletePlayerTurn();
            }
        }

        private void OnCharacterTurnCompleted(CharacterTurnCompleted turnCompleted)
        {
            if (_disposed || !IsEnabled) return;

            string characterId = string.IsNullOrWhiteSpace(turnCompleted.CharacterId)
                ? null
                : turnCompleted.CharacterId;

            ConvaiLogger.Debug(
                $"[TranscriptUIController] Character turn completed (id={characterId ?? "(all)"}, interrupted={turnCompleted.WasInterrupted})",
                LogCategory.UI);
            ActiveStrategy?.CompleteCharacterTurn(characterId);
        }

        #endregion

        #region UI Management

        private void UpdateActiveUI()
        {
            if (!IsEnabled) return;

            ITranscriptUI newActive = null;

            foreach (ITranscriptUI ui in _registeredUIs.Values)
            {
                bool shouldBeActive = MatchesMode(ui.Identifier, _currentMode);

                if (shouldBeActive != ui.IsActive)
                {
                    try
                    {
                        ui.SetActive(shouldBeActive);
                        ConvaiLogger.Debug(
                            $"[TranscriptUIController] Set UI '{ui.Identifier}' active={shouldBeActive} (mode={_currentMode})",
                            LogCategory.UI);
                    }
                    catch (Exception ex)
                    {
                        ConvaiLogger.Error(
                            $"[TranscriptUIController] Error setting active state for UI '{ui.Identifier}': {ex.Message}",
                            LogCategory.UI);
                    }
                }

                if (shouldBeActive && ui.IsActive) newActive = ui;
            }

            if (newActive != ActiveUI)
            {
                string previousId = ActiveUI?.Identifier ?? "none";
                string newId = newActive?.Identifier ?? "none";
                ActiveUI = newActive;
                ConvaiLogger.Debug($"[TranscriptUIController] Active UI changed from '{previousId}' to '{newId}'",
                    LogCategory.UI);
                ActiveUIChanged?.Invoke(ActiveUI);
            }
        }

        private static bool MatchesMode(string identifier, TranscriptUIMode mode)
        {
            if (string.IsNullOrEmpty(identifier))
                return false;

            return mode switch
            {
                TranscriptUIMode.Chat => identifier.Equals("Chat", StringComparison.OrdinalIgnoreCase) ||
                                         identifier.StartsWith("Chat", StringComparison.OrdinalIgnoreCase),
                TranscriptUIMode.Subtitle => identifier.Equals("Subtitle", StringComparison.OrdinalIgnoreCase) ||
                                             identifier.StartsWith("Subtitle", StringComparison.OrdinalIgnoreCase),
                TranscriptUIMode.QuestionAnswer => identifier.Equals("QuestionAnswer",
                                                       StringComparison.OrdinalIgnoreCase) ||
                                                   identifier.StartsWith("QuestionAnswer",
                                                       StringComparison.OrdinalIgnoreCase) ||
                                                   identifier.StartsWith("QA", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        /// <summary>
        ///     Sets the current mode by index (for settings panel integration).
        /// </summary>
        public void SetModeByIndex(int index)
        {
            CurrentMode = index switch
            {
                0 => TranscriptUIMode.Chat,
                1 => TranscriptUIMode.Subtitle,
                2 => TranscriptUIMode.QuestionAnswer,
                _ => TranscriptUIMode.Chat
            };
        }

        /// <summary>
        ///     Gets the current mode as an index (for settings panel integration).
        /// </summary>
        public int GetModeIndex()
        {
            return _currentMode switch
            {
                TranscriptUIMode.Chat => 0,
                TranscriptUIMode.Subtitle => 1,
                TranscriptUIMode.QuestionAnswer => 2,
                _ => 0
            };
        }

        #endregion
    }
}
