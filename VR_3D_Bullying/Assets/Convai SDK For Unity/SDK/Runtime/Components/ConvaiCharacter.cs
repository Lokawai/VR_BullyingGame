using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using Convai.Runtime.Services.CharacterLocator;
using Convai.Shared;
using Convai.Shared.DependencyInjection;
using Convai.Shared.Interfaces;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Canonical per-character API for conversation lifecycle and character-specific runtime events.
    ///     Designed for strict dependency injection with no service locator fallback.
    /// </summary>
    /// <remarks>
    ///     This component replaces the legacy ConvaiCharacter with a cleaner, focused implementation:
    ///     - ~150 lines vs ~1000+ lines in legacy ConvaiCharacter
    ///     - Strict DI: requires Inject() before use, no fallback patterns
    ///     - Uses room connection/audio services for transport operations
    ///     - Event-based communication via IEventHub
    ///     Usage:
    ///     1. Add to a GameObject in scene
    ///     2. Configure CharacterId and CharacterName in Inspector
    ///     3. ConvaiManager bootstraps and injects dependencies
    ///     4. Call StartConversationAsync() to begin conversation
    ///     Remote Audio Control:
    ///     By default, remote audio is ENABLED for all characters.
    ///     To disable audio playback for a character (text-only mode), call DisableRemoteAudio() or
    ///     SetRemoteAudioEnabled(false).
    ///     Text-only mode reduces bandwidth usage while still receiving transcript responses from the character.
    /// </remarks>
    [AddComponentMenu("Convai/Convai Character")]
    public class ConvaiCharacter : MonoBehaviour, IConvaiCharacterAgent, IInjectable, ICharacterIdentitySource
    {
        #region Serialized Fields

        [Header("Character Configuration")]
        [SerializeField]
        [Tooltip("The unique Character ID from your Convai dashboard (https://convai.com). " +
                 "This is required for connecting to the character.")]
        private string _characterId;

        [SerializeField] [Tooltip("Display name for this character, used in transcripts and debug logs.")]
        private string _characterName;

        [SerializeField] private Color _nameTagColor = Color.white;

        [Header("Behavior")]
#pragma warning disable CS0649
        [SerializeField]
        [Tooltip("If enabled, the character will automatically start a conversation after initialization. " +
                 "The connection waits for ConvaiRoomManager.Start() to complete before connecting.")]
        private bool _autoConnect;
#pragma warning restore CS0649

        [Header("Remote Audio (Default ON)")]
        [SerializeField]
        [Tooltip("If enabled, remote audio playback will be enabled for this character after dependency injection. " +
                 "Default is ON. Disable for text-only mode. You can also toggle at runtime via EnableRemoteAudio()/DisableRemoteAudio()/ToggleRemoteAudio().")]
        private bool _enableRemoteAudio = true;

        [SerializeField] [Tooltip("If enabled, this character attempts to resume previous sessions when reconnecting.")]
        private bool _enableSessionResume = true;

        [Header("Connection Settings")]
        [SerializeField]
        [Tooltip("Timeout in seconds to wait for the character ready signal after connection. " +
                 "If the character doesn't become ready within this time, StartConversationAsync returns false. " +
                 "Set to 0 to disable timeout (wait indefinitely).")]
        [Range(0, 120)]
        private float _characterReadyTimeoutSeconds = 30f;

        #endregion

        #region Dependencies

        private IEventHub _eventHub;
        private IConvaiRoomConnectionService _connectionService;
        private IConvaiRoomAudioService _audioService;
        private IConvaiCharacterLocatorService _locatorService;
        private ILogger _logger;

        private bool _isRegisteredWithLocator;

        private SubscriptionToken _ttsTextToken;
        private SubscriptionToken _speechStateToken;
        private SubscriptionToken _characterReadyToken;
        private SubscriptionToken _turnCompletedToken;
        private SubscriptionToken _emotionToken;

        private volatile bool _isSpeaking;
        private readonly object _emotionStateLock = new();
        private string _currentEmotion;
        private int _currentEmotionIntensity;

        private Task _toggleTask;
        private readonly object _toggleLock = new();

        private bool _isCharacterReady;
        private readonly object _characterReadyLock = new();

        private CancellationTokenSource _destroyCts;

        private TaskCompletionSource<bool> _characterReadyTcs;
        private readonly object _characterReadyTcsLock = new();

        #endregion

        #region Public Properties

        /// <summary>Character ID configured in the Convai dashboard.</summary>
        public string CharacterId => _characterId;

        /// <summary>Display name for the character.</summary>
        public string CharacterName => _characterName;

        /// <summary>Name tag color for transcript display.</summary>
        public Color NameTagColor => _nameTagColor;

        /// <summary>
        ///     Current session state from the connection service.
        ///     Replaces the legacy character state property for direct room state access.
        /// </summary>
        public SessionState SessionState => _connectionService?.CurrentState ?? SessionState.Disconnected;

        /// <summary>
        ///     Whether the character has received the bot-ready signal from the server.
        ///     This is set only by the CharacterReady domain event, not by room connection.
        /// </summary>
        public bool IsCharacterReady
        {
            get
            {
                lock (_characterReadyLock) return _isCharacterReady;
            }
            private set
            {
                bool changed;
                lock (_characterReadyLock)
                {
                    changed = _isCharacterReady != value;
                    _isCharacterReady = value;
                }

                if (changed)
                {
                    if (value)
                    {
                        lock (_characterReadyTcsLock) _characterReadyTcs?.TrySetResult(true);
                        OnCharacterReady?.Invoke();
                    }
                }
            }
        }

        /// <summary>
        ///     Whether the character is connected to the room. Does not imply character-ready state.
        /// </summary>
        public bool IsSessionConnected => SessionState == SessionState.Connected;

        /// <summary>Whether the character is in an active conversation (connected and character ready).</summary>
        public bool IsInConversation => IsSessionConnected && IsCharacterReady;

        /// <summary>Whether dependencies have been injected.</summary>
        public bool IsInjected { get; private set; }

        /// <summary>
        ///     Inspector-configured remote audio preference for this character.
        ///     This is applied after injection; default is true (audio enabled).
        /// </summary>
        public bool EnableRemoteAudioOnStart => _enableRemoteAudio;

        /// <summary>
        ///     Whether session resume should be attempted for this character.
        /// </summary>
        public bool EnableSessionResume => _enableSessionResume;

        /// <summary>
        ///     Timeout in seconds to wait for the character ready signal after connection.
        ///     Set to 0 to disable timeout (wait indefinitely). Default: 30 seconds.
        /// </summary>
        public float CharacterReadyTimeoutSeconds
        {
            get => _characterReadyTimeoutSeconds;
            set => _characterReadyTimeoutSeconds = Mathf.Max(0f, value);
        }

        /// <summary>Whether the character is currently speaking.</summary>
        public bool IsSpeaking => _isSpeaking;

        /// <summary>The most recently received character emotion label.</summary>
        public string CurrentEmotion
        {
            get
            {
                lock (_emotionStateLock) return _currentEmotion;
            }
        }

        /// <summary>The most recently received character emotion intensity (1-3).</summary>
        public int CurrentEmotionIntensity
        {
            get
            {
                lock (_emotionStateLock) return _currentEmotionIntensity;
            }
        }

        #endregion

        #region Events

        /// <summary>Raised when transcript text is received from the character.</summary>
        public event Action<string, bool> OnTranscriptReceived;

        /// <summary>Raised when the character begins speaking.</summary>
        public event Action OnSpeechStarted;

        /// <summary>Raised when the character stops speaking.</summary>
        public event Action OnSpeechStopped;

        /// <summary>Raised when the character completes its full turn.</summary>
        public event Action<bool> OnTurnCompleted;

        /// <summary>Raised when the character is ready to interact.</summary>
        public event Action OnCharacterReady;

        /// <summary>Raised when the session state changes.</summary>
        public event Action<SessionState> OnSessionStateChanged;

        /// <summary>Raised when the remote audio enabled state changes for this character.</summary>
        public event Action<bool> OnRemoteAudioEnabledChanged;

        /// <summary>Raised when the character emotion changes. Parameters: (emotion, intensity).</summary>
        public event Action<string, int> OnEmotionChanged;

        #endregion

        #region Remote Audio Control

        /// <summary>
        ///     Gets whether remote audio playback is enabled for this character.
        ///     When false (default), the character's audio track is unsubscribed and no audio packets are received.
        ///     When true, the character's audio track is subscribed and routed to the AudioSource.
        /// </summary>
        public bool IsRemoteAudioEnabled
        {
            get
            {
                if (!IsInjected || _audioService == null) return false;

                return _audioService.IsRemoteAudioEnabled(_characterId);
            }
        }

        /// <summary>
        ///     Enables or disables remote audio playback for this character.
        ///     When disabled (default), the character's audio track is unsubscribed (no audio packets received).
        ///     When enabled, the character's audio track is subscribed and routed to the AudioSource.
        /// </summary>
        /// <param name="enabled">True to enable audio playback; false to disable.</param>
        /// <returns>True when the operation succeeds; otherwise false.</returns>
        public bool SetRemoteAudioEnabled(bool enabled)
        {
            if (!IsInjected)
            {
                _logger?.Warning(
                    $"[ConvaiCharacter] [{_characterName}] Cannot set remote audio: dependencies not injected");
                return false;
            }

            if (_audioService == null)
            {
                _logger?.Warning(
                    $"[ConvaiCharacter] [{_characterName}] Cannot set remote audio: audio service not available");
                return false;
            }

            bool result = _audioService.SetRemoteAudioEnabled(_characterId, enabled);
            if (result)
            {
                _logger?.Debug(
                    $"[ConvaiCharacter] [{_characterName}] Remote audio {(enabled ? "enabled" : "disabled")}");
                OnRemoteAudioEnabledChanged?.Invoke(enabled);
            }

            return result;
        }

        /// <summary>
        ///     Enables remote audio playback for this character.
        ///     The character's audio track will be subscribed and routed to the AudioSource.
        /// </summary>
        /// <returns>True when the operation succeeds; otherwise false.</returns>
        public bool EnableRemoteAudio() => SetRemoteAudioEnabled(true);

        /// <summary>
        ///     Disables remote audio playback for this character.
        ///     The character's audio track will be unsubscribed (no audio packets received).
        /// </summary>
        /// <returns>True when the operation succeeds; otherwise false.</returns>
        public bool DisableRemoteAudio() => SetRemoteAudioEnabled(false);

        /// <summary>
        ///     Toggles remote audio playback for this character.
        ///     Intended for Unity UI button bindings.
        /// </summary>
        public void ToggleRemoteAudio() => SetRemoteAudioEnabled(!IsRemoteAudioEnabled);

        #endregion

        #region IConvaiCharacterAgent Implementation

        string IConvaiCharacterAgent.CharacterId => _characterId;
        string IConvaiCharacterAgent.CharacterName => _characterName;
        bool IConvaiCharacterAgent.EnableSessionResume => _enableSessionResume;

        void IConvaiCharacterAgent.SendTrigger(string triggerName, string triggerMessage) =>
            SendTrigger(triggerName, triggerMessage);

        void IConvaiCharacterAgent.SendDynamicInfo(string contextText) => SendDynamicInfo(contextText);

        void IConvaiCharacterAgent.UpdateTemplateKeys(Dictionary<string, string> templateKeys) =>
            UpdateTemplateKeys(templateKeys);

        #endregion

        #region Dependency Injection

        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            container.TryGet(out ILogger logger);
            Inject(
                container.Get<IEventHub>(),
                container.Get<IConvaiRoomConnectionService>(),
                container.Get<IConvaiRoomAudioService>(),
                container.Get<IConvaiCharacterLocatorService>(),
                logger);
        }

        /// <summary>
        ///     Injects dependencies into the character. Called by the ConvaiManager pipeline.
        ///     All parameters are required - no fallback to service locator.
        /// </summary>
        public void Inject(
            IEventHub eventHub,
            IConvaiRoomConnectionService connectionService,
            IConvaiRoomAudioService audioService,
            IConvaiCharacterLocatorService locatorService,
            ILogger logger = null)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _locatorService = locatorService ?? throw new ArgumentNullException(nameof(locatorService));
            _logger = logger;

            IsInjected = true;
            _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Dependencies injected");

            if (enabled && isActiveAndEnabled) InitializeAfterInjection();
        }

        #endregion

        #region Unity Lifecycle

        private void Awake() => ValidateSDKSetup();

        private void Start() => ValidateServiceContainerSetup();

        private void OnEnable()
        {
            _destroyCts?.Dispose();
            _destroyCts = new CancellationTokenSource();

            if (!IsInjected) return;

            InitializeAfterInjection();
        }

        /// <summary>
        ///     Called after dependency injection is complete. Handles registration and event subscription.
        ///     This is called either from OnEnable (if already injected) or from Inject() (if enabled).
        /// </summary>
        private void InitializeAfterInjection()
        {
            RegisterWithLocator();
            SubscribeToEvents();

            SetRemoteAudioEnabled(_enableRemoteAudio);

            _logger?.Debug(
                $"[ConvaiCharacter] [{_characterName}] Initialized after injection, autoConnect={_autoConnect}");
            if (_autoConnect && SessionState is SessionState.Disconnected or SessionState.Error)
            {
                _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Starting conversation automatically");
                StartConversationAsync().ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        Exception ex = task.Exception?.GetBaseException();
                        ConvaiLogger.Error($"[ConvaiCharacter] [{_characterName}] Auto-connect failed: {ex?.Message}",
                            LogCategory.Character);
                        _logger?.Error($"[ConvaiCharacter] [{_characterName}] Auto-connect failed: {ex?.Message}");
                    }
                    else if (task.IsCompletedSuccessfully && !task.Result)
                    {
                        ConvaiLogger.Warning($"[ConvaiCharacter] [{_characterName}] Auto-connect returned false",
                            LogCategory.Character);
                        _logger?.Warning($"[ConvaiCharacter] [{_characterName}] Auto-connect returned false");
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        private void OnDisable()
        {
            CancelPendingOperations();

            UnsubscribeFromEvents();
            UnregisterFromLocator();
        }

        private void OnDestroy()
        {
            CancelPendingOperations();
            _destroyCts?.Dispose();
            _destroyCts = null;

            UnsubscribeFromEvents();
            UnregisterFromLocator();
        }

        private void CancelPendingOperations()
        {
            if (_destroyCts != null && !_destroyCts.IsCancellationRequested)
            {
                try
                {
                    _destroyCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            lock (_characterReadyTcsLock)
            {
                _characterReadyTcs?.TrySetCanceled();
                _characterReadyTcs = null;
            }
        }

        private void OnValidate()
        {
            if (!UnityEngine.Application.isPlaying) ValidateEditorSetup();
        }

        /// <summary>
        ///     Validates SDK setup in the editor and shows warnings for missing components.
        /// </summary>
        private void ValidateEditorSetup()
        {
            if (FindFirstObjectByType<ConvaiManager>() == null)
            {
                ConvaiLogger.Warning(
                    $"[Convai SDK] {gameObject.name}: ConvaiManager not found in scene.\n" +
                    "Add it via: GameObject > Convai > Setup Required Components\n" +
                    "Or manually add the ConvaiManager component to a GameObject.",
                    LogCategory.Character);
            }
        }

        /// <summary>
        ///     Validates that the Convai SDK is properly set up at runtime.
        ///     Logs actionable error messages if required components are missing.
        /// </summary>
        private void ValidateSDKSetup()
        {
            List<string> errors = new();

            if (FindFirstObjectByType<ConvaiManager>() == null)
            {
                errors.Add(
                    "ConvaiManager not found in scene.\n" +
                    "   → Add ConvaiManager to your scene (GameObject > Convai > Setup Required Components)\n" +
                    "   → It auto-creates required bootstrap and room components");
            }

            if (errors.Count > 0)
            {
                string fullError = $"[Convai SDK Setup Error] {gameObject.name} ({_characterName}):\n\n" +
                                   string.Join("\n\n", errors.Select((e, i) => $"{i + 1}. {e}")) +
                                   "\n\n📖 For setup instructions, see: https://docs.convai.com/unity/quickstart" +
                                   "\n💡 Quick fix: Use 'GameObject > Convai > Setup Required Components' menu";

                ConvaiLogger.Error(fullError, LogCategory.Character);
            }
        }

        private void ValidateServiceContainerSetup()
        {
            if (FindFirstObjectByType<ConvaiManager>() == null) return;

            if (ConvaiServiceLocator.IsInitialized) return;

            string fullError = $"[Convai SDK Setup Error] {gameObject.name} ({_characterName}):\n\n" +
                               "1. Convai service container is not initialized after startup.\n" +
                               "   → Ensure ConvaiManager is active and enabled in the scene\n" +
                               "   → Check console logs for bootstrap initialization errors\n\n" +
                               "📖 For setup instructions, see: https://docs.convai.com/unity/quickstart" +
                               "\n💡 Quick fix: Use 'GameObject > Convai > Setup Required Components' menu";

            ConvaiLogger.Error(fullError, LogCategory.Character);
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Starts a conversation with this character.
        ///     Connects to the room if not already connected.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation. Combined with component lifetime token.</param>
        /// <returns>True if conversation started successfully; otherwise false.</returns>
        public async Task<bool> StartConversationAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_characterId))
            {
                ConvaiLogger.Error($"[ConvaiCharacter] [{_characterName}] Cannot start: CharacterId is empty. " +
                                   "Set the Character ID in the Inspector (find it on your Convai dashboard at https://convai.com).",
                    LogCategory.Character);
                return false;
            }

            if (!IsInjected)
            {
                ConvaiLogger.Error($"[ConvaiCharacter] [{_characterName}] Cannot start: dependencies not injected. " +
                                   "Ensure ConvaiManager is in the scene and has run before calling StartConversationAsync().",
                    LogCategory.Character);
                return false;
            }

            if (_connectionService == null)
            {
                _logger?.Error($"[ConvaiCharacter] [{_characterName}] Cannot start: _connectionService is null");
                return false;
            }

            if (SessionState == SessionState.Connected)
            {
                _logger?.Debug(
                    $"[ConvaiCharacter] [{_characterName}] StartConversationAsync ignored: already connected");
                return true;
            }

            if (SessionState == SessionState.Disconnecting)
            {
                _logger?.Warning($"[ConvaiCharacter] [{_characterName}] Cannot start while disconnecting");
                return false;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _destroyCts?.Token ?? CancellationToken.None);
            CancellationToken linkedToken = linkedCts.Token;

            _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Starting conversation...");

            IsCharacterReady = false;

            TaskCompletionSource<bool> readyTcs = new();
            lock (_characterReadyTcsLock)
            {
                _characterReadyTcs?.TrySetCanceled();
                _characterReadyTcs = readyTcs;
            }

            try
            {
                linkedToken.ThrowIfCancellationRequested();

                _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Awaiting connection service...");
                bool connected = await _connectionService.ConnectAsync(linkedToken);

                if (!connected)
                {
                    _logger?.Error($"[ConvaiCharacter] [{_characterName}] Connection service returned false");
                    return false;
                }

                linkedToken.ThrowIfCancellationRequested();

                _logger?.Info(
                    $"[ConvaiCharacter] [{_characterName}] Connection successful, waiting for character ready signal...");

                return await WaitForCharacterReadyAsync(readyTcs, linkedToken);
            }
            catch (OperationCanceledException)
            {
                _logger?.Debug($"[ConvaiCharacter] [{_characterName}] StartConversationAsync cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.Error($"[ConvaiCharacter] [{_characterName}] Connection failed with exception: {ex.Message}");
                return false;
            }
            finally
            {
                lock (_characterReadyTcsLock)
                {
                    if (_characterReadyTcs == readyTcs)
                        _characterReadyTcs = null;
                }
            }
        }

        /// <summary>
        ///     Waits for the CharacterReady signal with optional timeout.
        /// </summary>
        private async Task<bool> WaitForCharacterReadyAsync(TaskCompletionSource<bool> readyTcs,
            CancellationToken cancellationToken) =>
            await WaitForCharacterReadyInternalAsync(readyTcs, _characterReadyTimeoutSeconds, cancellationToken);

        /// <summary>
        ///     Internal implementation that waits for the CharacterReady signal with a specified timeout.
        /// </summary>
        private async Task<bool> WaitForCharacterReadyInternalAsync(TaskCompletionSource<bool> readyTcs,
            float timeoutSeconds, CancellationToken cancellationToken)
        {
            if (IsCharacterReady)
            {
                _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Already character ready");
                return true;
            }

            if (timeoutSeconds <= 0f)
            {
                _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Waiting indefinitely for character ready...");
                try
                {
                    using (cancellationToken.Register(() => readyTcs.TrySetCanceled())) return await readyTcs.Task;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            int timeoutMs = (int)(timeoutSeconds * 1000);
            _logger?.Debug(
                $"[ConvaiCharacter] [{_characterName}] Waiting for character ready (timeout: {timeoutSeconds}s)...");

            using CancellationTokenSource timeoutCts = new(timeoutMs);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            try
            {
                using (combinedCts.Token.Register(() => readyTcs.TrySetCanceled())) return await readyTcs.Task;
            }
            catch (OperationCanceledException)
            {
                if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    _logger?.Warning(
                        $"[ConvaiCharacter] [{_characterName}] Character ready timeout after {timeoutSeconds}s");
                }

                return false;
            }
        }

        /// <summary>
        ///     Waits for the character to become ready. Returns immediately if already ready.
        /// </summary>
        /// <param name="timeoutSeconds">
        ///     Optional timeout in seconds. If null, uses the configured CharacterReadyTimeoutSeconds.
        ///     Use 0 or negative to wait indefinitely.
        /// </param>
        /// <param name="cancellationToken">Token to cancel the wait operation.</param>
        /// <returns>True if character became ready; false if timeout expired or cancelled.</returns>
        public async Task<bool> WaitForCharacterReadyAsync(float? timeoutSeconds = null,
            CancellationToken cancellationToken = default)
        {
            if (IsCharacterReady) return true;

            if (SessionState != SessionState.Connected && SessionState != SessionState.Connecting)
            {
                _logger?.Warning(
                    $"[ConvaiCharacter] [{_characterName}] Cannot wait for CharacterReady: not connected (state={SessionState})");
                return false;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _destroyCts?.Token ?? CancellationToken.None);

            TaskCompletionSource<bool> readyTcs = new();
            lock (_characterReadyTcsLock)
            {
                if (_characterReadyTcs != null)
                    readyTcs = _characterReadyTcs;
                else
                    _characterReadyTcs = readyTcs;
            }

            try
            {
                float timeout = timeoutSeconds ?? _characterReadyTimeoutSeconds;
                return await WaitForCharacterReadyInternalAsync(readyTcs, timeout, linkedCts.Token);
            }
            finally
            {
                lock (_characterReadyTcsLock)
                {
                    if (_characterReadyTcs == readyTcs)
                        _characterReadyTcs = null;
                }
            }
        }

        /// <summary>
        ///     Ends the current conversation gracefully.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation. Combined with component lifetime token.</param>
        public async Task StopConversationAsync(CancellationToken cancellationToken = default)
        {
            if (SessionState == SessionState.Disconnected) return;

            lock (_characterReadyTcsLock)
            {
                _characterReadyTcs?.TrySetCanceled();
                _characterReadyTcs = null;
            }

            IsCharacterReady = false;
            _isSpeaking = false;
            ResetEmotionState();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _destroyCts?.Token ?? CancellationToken.None);

            try
            {
                await _connectionService.DisconnectAsync(cancellationToken: linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger?.Debug($"[ConvaiCharacter] [{_characterName}] StopConversationAsync cancelled");
            }
            catch (Exception ex)
            {
                _logger?.Error($"[ConvaiCharacter] [{_characterName}] Disconnect failed: {ex.Message}");
            }
        }

        /// <summary>
        ///     Sends a trigger event to the conversation backend.
        /// </summary>
        /// <param name="triggerName">Name of the trigger to send.</param>
        /// <param name="triggerMessage">Optional message payload.</param>
        public void SendTrigger(string triggerName, string triggerMessage = null)
        {
            if (!IsInConversation)
            {
                _logger?.Warning($"[ConvaiCharacter] [{_characterName}] Cannot send trigger: not in conversation");
                return;
            }

            if (!_connectionService.SendTrigger(triggerName, triggerMessage))
            {
                _logger?.Warning(
                    $"[ConvaiCharacter] [{_characterName}] Connection not ready for trigger {triggerName}");
            }
        }

        /// <summary>
        ///     Sends dynamic context information to the backend.
        ///     This is injected as a context update for the character.
        /// </summary>
        /// <param name="contextText">The dynamic context text to send.</param>
        public void SendDynamicInfo(string contextText)
        {
            if (string.IsNullOrEmpty(contextText))
            {
                _logger?.Warning($"[ConvaiCharacter] [{_characterName}] Cannot send empty dynamic info");
                return;
            }

            if (!IsInConversation)
            {
                _logger?.Warning($"[ConvaiCharacter] [{_characterName}] Cannot send dynamic info: not in conversation");
                return;
            }

            if (!_connectionService.SendDynamicInfo(contextText))
            {
                _logger?.Warning($"[ConvaiCharacter] [{_characterName}] Connection not ready for dynamic info");
                return;
            }

            _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Sent dynamic info: {contextText}");
        }

        /// <summary>
        ///     Updates template keys for narrative design placeholder resolution.
        ///     Template keys like {PlayerName} in objectives will be replaced with the corresponding value.
        /// </summary>
        /// <param name="templateKeys">Dictionary of key-value pairs to update.</param>
        public void UpdateTemplateKeys(Dictionary<string, string> templateKeys)
        {
            if (templateKeys == null || templateKeys.Count == 0)
            {
                _logger?.Warning($"[ConvaiCharacter] [{_characterName}] Cannot update empty template keys");
                return;
            }

            if (!IsInConversation)
            {
                _logger?.Warning(
                    $"[ConvaiCharacter] [{_characterName}] Cannot update template keys: not in conversation");
                return;
            }

            if (!_connectionService.UpdateTemplateKeys(templateKeys))
            {
                _logger?.Warning($"[ConvaiCharacter] [{_characterName}] Connection not ready for template keys");
                return;
            }

            _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Updated {templateKeys.Count} template keys");
        }

        /// <summary>
        ///     Programmatically configures the character ID and name.
        /// </summary>
        /// <param name="characterId">The character ID from the Convai dashboard.</param>
        /// <param name="characterName">Optional display name (defaults to characterId).</param>
        /// <exception cref="InvalidOperationException">Thrown if called during an active conversation.</exception>
        public void Configure(string characterId, string characterName = null)
        {
            if (IsInConversation)
            {
                throw new InvalidOperationException(
                    "Cannot configure character while in conversation. Call StopConversationAsync() first.");
            }

            _characterId = characterId;
            _characterName = characterName ?? characterId;
        }

        /// <summary>
        ///     Resets the character ready state and clears internal tracking.
        ///     Call this when you want to allow a fresh connection attempt after an error.
        /// </summary>
        /// <returns>True if the reset was performed; false if already disconnected.</returns>
        public bool Reset()
        {
            if (SessionState == SessionState.Error || SessionState == SessionState.Disconnected)
            {
                _logger?.Debug(
                    $"[ConvaiCharacter] [{_characterName}] Resetting character ready state from {SessionState}");
                IsCharacterReady = false;
                return true;
            }

            _logger?.Debug(
                $"[ConvaiCharacter] [{_characterName}] Reset called but state is {SessionState}, not Error or Disconnected");
            return false;
        }

        /// <summary>
        ///     Resets the character from Error state and immediately attempts to reconnect.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if reset and reconnection succeeded; false otherwise.</returns>
        public async Task<bool> ResetAndRetryAsync(CancellationToken cancellationToken = default)
        {
            if (SessionState != SessionState.Error)
            {
                _logger?.Warning(
                    $"[ConvaiCharacter] [{_characterName}] ResetAndRetryAsync called but state is {SessionState}, not Error");
                return false;
            }

            _logger?.Info($"[ConvaiCharacter] [{_characterName}] Resetting from Error and retrying connection...");
            IsCharacterReady = false;
            return await StartConversationAsync(cancellationToken);
        }

        /// <summary>
        ///     Toggles the conversation state. Starts if disconnected, ends if connected.
        ///     This method is provided for backward compatibility with UI button bindings.
        ///     Note: This does NOT toggle remote audio playback. Use EnableRemoteAudio/DisableRemoteAudio/ToggleRemoteAudio for
        ///     that.
        ///     Toggle operations are serialized to prevent overlapping Start/Stop calls.
        /// </summary>
        public void ToggleSpeech()
        {
            lock (_toggleLock)
            {
                if (_toggleTask != null && !_toggleTask.IsCompleted)
                {
                    _logger?.Warning(
                        $"[ConvaiCharacter] [{_characterName}] Toggle operation already in progress, ignoring duplicate call");
                    return;
                }

                if (SessionState is SessionState.Disconnected or SessionState.Error)
                {
                    _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Starting conversation after toggle");
                    _toggleTask = ToggleStartAsync();
                }
                else if (IsSessionConnected)
                {
                    _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Stopping conversation after toggle");
                    _toggleTask = ToggleStopAsync();
                }
                else
                {
                    _logger?.Warning(
                        $"[ConvaiCharacter] [{_characterName}] Toggle called in transitional state {SessionState}, ignoring");
                }
            }
        }

        /// <summary>
        ///     Async toggle variant that returns a task for awaiting.
        /// </summary>
        public Task ToggleSpeechAsync()
        {
            ToggleSpeech();
            lock (_toggleLock) return _toggleTask ?? Task.CompletedTask;
        }

        private async Task ToggleStartAsync()
        {
            try
            {
                bool remoteAudioEnabled = IsRemoteAudioEnabled;
                bool started = await StartConversationAsync();
                if (started && !remoteAudioEnabled)
                {
                    _logger?.Info(
                        $"[ConvaiCharacter] [{_characterName}] Connected in text-only mode (remote audio disabled). " +
                        "Call EnableRemoteAudio() / ToggleRemoteAudio() to hear speech.");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"[ConvaiCharacter] [{_characterName}] Toggle start failed: {ex.Message}");
            }
        }

        private async Task ToggleStopAsync()
        {
            try
            {
                await StopConversationAsync();
            }
            catch (Exception ex)
            {
                _logger?.Error($"[ConvaiCharacter] [{_characterName}] Toggle stop failed: {ex.Message}");
            }
        }

        #endregion

        #region Private Helpers

        private void RegisterWithLocator()
        {
            if (_isRegisteredWithLocator || _locatorService == null) return;

            _locatorService.AddCharacter(this);
            _isRegisteredWithLocator = true;
            _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Registered with locator");
        }

        private void UnregisterFromLocator()
        {
            if (!_isRegisteredWithLocator || _locatorService == null) return;

            _locatorService.RemoveCharacter(this);
            _isRegisteredWithLocator = false;
        }

        private void SubscribeToEvents()
        {
            if (_eventHub != null)
            {
                _ttsTextToken = _eventHub.Subscribe<CharacterTtsTextChunk>(OnCharacterTtsTextReceived);
                _speechStateToken = _eventHub.Subscribe<CharacterSpeechStateChanged>(OnSpeechStateChanged);
                _characterReadyToken = _eventHub.Subscribe<CharacterReady>(OnCharacterReadyReceived);
                _turnCompletedToken = _eventHub.Subscribe<CharacterTurnCompleted>(OnCharacterTurnCompleted);
                _emotionToken = _eventHub.Subscribe<CharacterEmotionChanged>(OnCharacterEmotionReceived);
            }
            else
                _logger?.Warning("[ConvaiCharacter] EventHub is null - cannot subscribe to events");

            if (_connectionService != null) _connectionService.OnSessionStateChanged += OnSessionStateChangedInternal;
        }

        private void UnsubscribeFromEvents()
        {
            if (_eventHub != null)
            {
                if (_ttsTextToken != default) _eventHub.Unsubscribe(_ttsTextToken);
                if (_speechStateToken != default) _eventHub.Unsubscribe(_speechStateToken);
                if (_characterReadyToken != default) _eventHub.Unsubscribe(_characterReadyToken);
                if (_turnCompletedToken != default) _eventHub.Unsubscribe(_turnCompletedToken);
                if (_emotionToken != default) _eventHub.Unsubscribe(_emotionToken);
            }

            _ttsTextToken = default;
            _speechStateToken = default;
            _characterReadyToken = default;
            _turnCompletedToken = default;
            _emotionToken = default;

            if (_connectionService != null) _connectionService.OnSessionStateChanged -= OnSessionStateChangedInternal;
        }

        private void OnSessionStateChangedInternal(SessionStateChanged e)
        {
            _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Session state changed: {e.OldState} -> {e.NewState}");

            if (e.NewState == SessionState.Disconnected || e.NewState == SessionState.Error)
            {
                IsCharacterReady = false;
                _isSpeaking = false;
                ResetEmotionState();
            }

            OnSessionStateChanged?.Invoke(e.NewState);
        }

        private void OnCharacterReadyReceived(CharacterReady e)
        {
            bool matchesCharacterId = string.Equals(e.CharacterId, _characterId, StringComparison.OrdinalIgnoreCase);
            bool matchesParticipantId =
                !string.IsNullOrEmpty(e.ParticipantId) &&
                e.ParticipantId.Contains(_characterId, StringComparison.OrdinalIgnoreCase);

            if (!matchesCharacterId && !matchesParticipantId) return;

            _logger?.Info($"[ConvaiCharacter] [{_characterName}] Received character ready signal");
            IsCharacterReady = true;
        }

        private void OnCharacterTtsTextReceived(CharacterTtsTextChunk chunk)
        {
            if (!IsMatchingCharacter(chunk.ParticipantId)) return;

            OnTranscriptReceived?.Invoke(chunk.Text, chunk.IsFinal);
        }

        private void OnSpeechStateChanged(CharacterSpeechStateChanged e)
        {
            if (!string.Equals(e.CharacterId, _characterId, StringComparison.OrdinalIgnoreCase)) return;

            _isSpeaking = e.IsSpeaking;

            if (e.IsSpeaking)
                OnSpeechStarted?.Invoke();
            else
                OnSpeechStopped?.Invoke();
        }

        private void OnCharacterEmotionReceived(CharacterEmotionChanged e)
        {
            if (!string.Equals(e.CharacterId, _characterId, StringComparison.OrdinalIgnoreCase)) return;

            SetEmotionState(e.Emotion, e.Intensity);
            OnEmotionChanged?.Invoke(e.Emotion, e.Intensity);
        }

        private void SetEmotionState(string emotion, int intensity)
        {
            lock (_emotionStateLock)
            {
                _currentEmotion = emotion;
                _currentEmotionIntensity = intensity;
            }
        }

        private void ResetEmotionState()
        {
            lock (_emotionStateLock)
            {
                _currentEmotion = null;
                _currentEmotionIntensity = 0;
            }
        }

        private void OnCharacterTurnCompleted(CharacterTurnCompleted e)
        {
            bool matchesCharacterId = string.Equals(e.CharacterId, _characterId, StringComparison.OrdinalIgnoreCase);
            bool matchesParticipantId = !string.IsNullOrEmpty(e.ParticipantId) &&
                                        e.ParticipantId.Contains(_characterId, StringComparison.OrdinalIgnoreCase);

            if (!matchesCharacterId && !matchesParticipantId) return;

            _logger?.Debug($"[ConvaiCharacter] [{_characterName}] Turn completed (interrupted={e.WasInterrupted})");
            OnTurnCompleted?.Invoke(e.WasInterrupted);
        }

        private bool IsMatchingCharacter(string participantIdOrCharacterId)
        {
            return !string.IsNullOrEmpty(participantIdOrCharacterId) &&
                   participantIdOrCharacterId.Contains(_characterId, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
