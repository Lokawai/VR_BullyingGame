using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Domain.Identity;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Audio;
using Convai.Infrastructure.Networking.Models;
using Convai.Infrastructure.Networking.Services;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Persistence;
using Convai.Infrastructure.Protocol.Messages;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Runtime.Networking.Media;
using Convai.Runtime.Persistence;
using Convai.Runtime.Room;
using Convai.Runtime.Vision;
using Convai.Shared;
using Convai.Shared.Abstractions;
using Convai.Shared.DependencyInjection;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;
using ISessionPersistence = Convai.Domain.Abstractions.ISessionPersistence;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Unity MonoBehaviour that manages Convai room connections and audio services.
    ///     Implements IConvaiRoomConnectionService and IConvaiRoomAudioService.
    ///     Owns room connection lifecycle, retry/rejoin bookkeeping, scene discovery, and audio preferences.
    /// </summary>
    public partial class ConvaiRoomManager : MonoBehaviour, IConvaiRoomConnectionService, IConvaiRoomAudioService,
        IInjectable
    {
        private static readonly TimeSpan[] _defaultRetryDelays =
        {
            TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)
        };

        #region IConvaiRoomAudioService Properties

        /// <inheritdoc />
        public bool IsMicMuted =>
            _audioTrackManager?.IsMicMuted ??
            _audioManager?.IsMicMuted ??
            _convaiRoomController?.IsMicMuted ??
            false;

        #endregion

        /// <inheritdoc />
        public bool RequiresUserGestureForAudio =>
            RealtimeTransportFactory.GetCapabilities().RequiresUserGestureForAudio;

        /// <inheritdoc />
        public bool IsAudioPlaybackActive
        {
            get
            {
                IRealtimeTransport transport = TryGetTransport();
                return transport == null || transport.AudioState.IsAudioPlaybackActive;
            }
        }

        /// <inheritdoc />
        public bool CanEnableAudioPlayback
        {
            get
            {
                IRealtimeTransport transport = TryGetTransport();
                return transport == null || transport.CanEnableAudio();
            }
        }

        /// <inheritdoc />
        public void EnableAudioPlayback()
        {
            IRealtimeTransport transport = TryGetTransport();
            transport?.EnableAudio();
        }

        /// <summary>
        ///     Enables audio playback and immediately attempts to start microphone capture.
        ///     Useful for WebGL permission flows driven by a single user gesture.
        /// </summary>
        public void EnableAudioAndStartListening()
        {
            EnableAudioPlayback();
            StartListeningAsync().ContinueWith(
                static t => ConvaiLogger.Error(
                    $"[ConvaiRoomManager] StartListeningAsync failed: {t.Exception?.GetBaseException().Message}",
                    LogCategory.SDK),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        ///     Starts microphone capture/publication using the default microphone index.
        ///     This wrapper exists so Unity UI events can trigger mic start without async signatures.
        /// </summary>
        public void StartListening()
        {
            if (RequiresUserGestureForAudio && !IsAudioPlaybackActive)
            {
                EnableAudioAndStartListening();
                return;
            }

            StartListeningAsync().ContinueWith(
                static t => ConvaiLogger.Error(
                    $"[ConvaiRoomManager] StartListeningAsync failed: {t.Exception?.GetBaseException().Message}",
                    LogCategory.SDK),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        #region Serialized Fields

        /// <summary>Gets the connection type used for character sessions.</summary>
        [field: SerializeField]
        [field: Tooltip("The connection type for character sessions (Audio or Video)")]
        public ConvaiConnectionType ConnectionType { get; private set; } = ConvaiConnectionType.Audio;

        /// <summary>Gets the LLM provider used for character responses.</summary>
        [field: SerializeField]
        [field: Tooltip("The LLM provider for character responses")]
        public ConvaiLLMProvider LLMProvider { get; private set; } = ConvaiLLMProvider.Dynamic;

        /// <summary>Gets the base URL of the Convai core server (without endpoint path).</summary>
        [field: SerializeField]
        [field:
            Tooltip(
                "The base URL of the Convai core server (without endpoint path). Leave blank to use ConvaiSettings.ServerUrl. Example: https://api.convai.com")]
        public string CoreServerBaseURL { get; private set; }

        /// <summary>Gets the core server endpoint used for room connections.</summary>
        [field: SerializeField]
        [field: Tooltip("The endpoint to use for room connections")]
        public ConvaiServerEndpoint ServerEndpoint { get; private set; } = ConvaiServerEndpoint.Connect;

        /// <summary>Gets a value indicating whether the manager connects automatically on <see cref="Start" />.</summary>
        [field: SerializeField]
        [field:
            Tooltip(
                "If false, connection will not start automatically. Use ConnectAsync() to initiate connection manually.")]
        public bool ConnectOnStart { get; private set; } = true;

        // These fields must always be serialized to maintain consistent serialization layout across platforms.
        // They are read/written only in ConvaiRoomManager.Editor.cs (editor-only partial).
#pragma warning disable CS0414 // assigned but never read — used in editor partial class
        [SerializeField] [HideInInspector]
        private ConvaiConnectionType _lastValidatedConnectionType = ConvaiConnectionType.Audio;
#pragma warning restore CS0414

        [SerializeField] [HideInInspector] private bool _visionSetupPrompted;

        // _visionSetupQueued is declared in ConvaiRoomManager.Editor.cs (editor-only partial)

        #endregion

        #region Public Properties

        private string ResolvedCoreServerBaseURL =>
            string.IsNullOrWhiteSpace(CoreServerBaseURL) ? ConvaiSettings.Instance.ServerUrl : CoreServerBaseURL;

        /// <summary>
        ///     Gets the full Core Server URL with the endpoint path appended.
        /// </summary>
        public string CoreServerURL => ServerEndpoint.BuildUrl(ResolvedCoreServerBaseURL);

        /// <summary>Gets the current player agent, when available.</summary>
        public IConvaiPlayerAgent Player { get; private set; }

        /// <summary>Gets a map of discovered scene characters to their audio data.</summary>
        public IReadOnlyDictionary<IConvaiCharacterAgent, CharacterAudioData> CharacterToParticipantMap =>
            _sceneDiscovery?.CharacterToParticipantMap;

        /// <summary>Gets the list of discovered scene character agents.</summary>
        public IReadOnlyList<IConvaiCharacterAgent> CharacterList => _characterList;

        private List<IConvaiCharacterAgent> _characterList;

        #endregion

        #region IConvaiRoomConnectionService Events

        /// <inheritdoc />
        public event Action Connected;

        /// <inheritdoc />
        public event Action ConnectionFailed;

        /// <inheritdoc />
        public event Action<SessionStateChanged> OnSessionStateChanged;

        #endregion

        #region IConvaiRoomAudioService Events

        /// <inheritdoc />
        public event Action<bool> MicMuteChanged;

        /// <inheritdoc />
        public event Action<string, bool> RemoteAudioEnabledChanged;

        #endregion

        #region IConvaiRoomConnectionService Properties

        /// <inheritdoc />
        public SessionState CurrentState => _sessionStateMachine?.CurrentState ?? SessionState.Disconnected;

        /// <inheritdoc />
        public bool IsConnected => _convaiRoomController?.IsConnectedToRoom ?? false;

        /// <inheritdoc />
        public bool HasRoomDetails => _convaiRoomController?.HasRoomDetails ?? false;

        /// <inheritdoc />
        /// <remarks>
        ///     Returns the platform-agnostic room facade. On native platforms, this wraps LiveKit.Room.
        ///     On WebGL, this wraps the browser-based room implementation.
        ///     Use this property instead of accessing LiveKit.Room directly for cross-platform compatibility.
        /// </remarks>
        public IRoomFacade CurrentRoom
        {
            get
            {
                if (_convaiRoomController?.CurrentRoom != null) return _convaiRoomController.CurrentRoom;
                return null;
            }
        }

        /// <inheritdoc />
        public RTVIHandler RtvHandler => _convaiRoomController?.RTVIHandler;

        /// <inheritdoc />
        public bool SendTrigger(string triggerName, string triggerMessage = null)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVITriggerMessage(triggerName, triggerMessage));
            return true;
        }

        /// <inheritdoc />
        public bool SendDynamicInfo(string contextText)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVIUpdateDynamicInfo(new DynamicInfo { Text = contextText }));
            return true;
        }

        /// <inheritdoc />
        public bool UpdateTemplateKeys(Dictionary<string, string> templateKeys)
        {
            RTVIHandler handler = RtvHandler;
            if (handler == null) return false;

            handler.SendData(new RTVIUpdateTemplateKeys(templateKeys));
            return true;
        }

        #endregion

        #region Private Fields

        private readonly object _connectionStateTcsLock = new();
        private readonly TaskCompletionSource<bool> _startCompletedTcs = new();
        private SceneCharacterDiscovery _sceneDiscovery;
        private RemoteAudioPreferenceManager _remoteAudioPreferences;

        private AudioTrackManager _audioTrackManager;

        private IConvaiRoomController _convaiRoomController;
        private ILogger _logger;
        private IServiceContainer _container;
        private IConvaiRoomControllerFactory _controllerFactory;
        private IMicrophoneSourceFactory _microphoneSourceFactory;

        private IRealtimeTransport TryGetTransport()
        {
            if (_container != null && _container.TryGet(out IRealtimeTransportAccessor accessor) &&
                accessor?.Transport != null) return accessor.Transport;
            return null;
        }

        private CharacterRegistryAdapter _characterRegistry;
        private PlayerSessionAdapter _playerSession;
        private ConfigurationProviderAdapter _configProvider;
        private MainThreadDispatcherAdapter _dispatcher;
        private IEndUserIdProvider _endUserIdProvider;

        private IEventHub _eventHub;
        private SubscriptionToken _characterReadyToken;
        private ISessionPersistence _sessionPersistence;
        private IConvaiAudioManager _audioManager;

        private TaskCompletionSource<bool> _connectionStateTcs;
        private Task<bool> _connectTask;
        private ConnectionContext _connectionContext = ConnectionContext.Empty;
        private bool _hasStartedOnce;
        private ReconnectPolicy _reconnectPolicy = ReconnectPolicy.Default;
        private ISessionStateMachine _sessionStateMachine;
        private ISessionService _sessionService;
        private INarrativeSectionNameResolver _sectionNameResolver;
        private IConvaiRuntimeSettingsService _runtimeSettingsService;
        private IMicrophoneDeviceService _microphoneDeviceService;

        private bool _isInjected;

        private static ConvaiRoomManager _instance;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Infrastructure component: injects before others so that services it registers
        ///     (IConvaiRoomConnectionService, IConvaiRoomAudioService, IConvaiAudioManager)
        ///     are available when downstream IInjectables resolve them.
        /// </summary>
        int IInjectable.InjectionOrder => -100;

        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            _container = container;

            // 1. Register self as room services
            if (!container.IsRegistered<IConvaiRoomConnectionService>())
                container.Register(ServiceDescriptor.Singleton<IConvaiRoomConnectionService>(this));

            if (!container.IsRegistered<IConvaiRoomAudioService>())
                container.Register(ServiceDescriptor.Singleton<IConvaiRoomAudioService>(this));

            // 2. Resolve own dependencies
            var eventHub = container.Get<IEventHub>();
            container.TryGet(out ISessionPersistence sessionPersistence);
            container.TryGet(out IConvaiAudioManager audioManager);
            container.TryGet(out IEndUserIdProvider endUserIdProvider);
            container.TryGet(out ISessionStateMachine sessionStateMachine);
            container.TryGet(out ISessionService sessionService);
            container.TryGet(out INarrativeSectionNameResolver sectionNameResolver);

            // 3. Resolve platform/factory services
            container.TryGet(out _controllerFactory);
            container.TryGet(out _microphoneSourceFactory);
            container.TryGet(out _runtimeSettingsService);
            container.TryGet(out _microphoneDeviceService);

            Inject(eventHub, sessionPersistence, audioManager,
                endUserIdProvider, sessionStateMachine, sessionService,
                sectionNameResolver);

            // 4. Register IConvaiAudioManager if not already registered (needs room services above)
            if (!container.IsRegistered<IConvaiAudioManager>())
            {
                container.Register(ServiceDescriptor.Singleton<IConvaiAudioManager>(c =>
                {
                    var connService = c.Get<IConvaiRoomConnectionService>();
                    var audioService = c.Get<IConvaiRoomAudioService>();
                    c.TryGet(out ILogger logger);
                    return new DefaultAudioManager(connService, audioService, logger);
                }));
            }
        }

        /// <summary>
        ///     Configures the reconnect policy. Call before any connection attempts.
        /// </summary>
        public void SetReconnectPolicy(ReconnectPolicy policy)
        {
            _reconnectPolicy = policy ?? ReconnectPolicy.Default;
            ConvaiLogger.Debug($"[ConvaiRoomManager] Reconnect policy set: {policy}", LogCategory.SDK);
        }

        /// <summary>
        ///     Injects dependencies into ConvaiRoomManager.
        ///     Called by the ConvaiManager pipeline during scene initialization.
        /// </summary>
        public void Inject(
            IEventHub eventHub,
            ISessionPersistence sessionPersistence = null,
            IConvaiAudioManager audioManager = null,
            IEndUserIdProvider endUserIdProvider = null,
            ISessionStateMachine sessionStateMachine = null,
            ISessionService sessionService = null,
            INarrativeSectionNameResolver sectionNameResolver = null)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub),
                "IEventHub is required for ConvaiRoomManager.");

            _sessionPersistence =
                sessionPersistence ?? new KeyValueStoreSessionPersistence(new PlayerPrefsKeyValueStore());
            _audioManager = audioManager;
            _endUserIdProvider = endUserIdProvider;
            _sectionNameResolver = sectionNameResolver;

            _logger = new ConvaiLogger();

            if (_sessionStateMachine != null)
                _sessionStateMachine.StateChanged -= OnSessionStateMachineStateChanged;

            _sessionStateMachine = sessionStateMachine ?? new SessionStateMachine(_eventHub, _logger);
            _sessionStateMachine.StateChanged += OnSessionStateMachineStateChanged;
            _sessionService = sessionService;
            _reconnectPolicy = ReconnectPolicy.Default;
            _connectionContext = ConnectionContext.Empty;

            _remoteAudioPreferences = new RemoteAudioPreferenceManager(null, _logger);
            _remoteAudioPreferences.RemoteAudioEnabledChanged += OnRemoteAudioPreferenceChanged;

            _sceneDiscovery = new SceneCharacterDiscovery(_logger, (characterId, enabled) =>
            {
                _remoteAudioPreferences.InitializePreference(characterId, enabled);
            });

            _characterReadyToken = _eventHub.Subscribe<CharacterReady>(HandleCharacterReadyEvent);

            _isInjected = true;
            ConvaiLogger.Debug("[ConvaiRoomManager] Dependencies injected via ConvaiManager pipeline", LogCategory.SDK);
        }

        #endregion

        #region Unity Lifecycle

        // Editor-only vision setup (OnValidate, QueueVisionSetupPrompt, etc.)
        // is defined in ConvaiRoomManager.Editor.cs

        private void Awake()
        {
            if (!_isInjected && ConvaiManager.Instance == null)
            {
                string errorMessage =
                    "[Convai SDK Setup Error] ConvaiRoomManager dependencies not injected!\n\n" +
                    "ConvaiRoomManager requires ConvaiManager setup to inject its dependencies.\n\n" +
                    "To fix this:\n" +
                    "1. Add ConvaiManager to your scene:\n" +
                    "   → Use menu: GameObject > Convai > Setup Required Components\n\n" +
                    "2. Verify ConvaiManager is active/enabled in the scene.\n\n" +
                    "📖 See: https://docs.convai.com/unity/quickstart";

                ConvaiLogger.Error(errorMessage, LogCategory.SDK);
                enabled = false;
                return;
            }

            if (transform.parent != null) transform.SetParent(null);

            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this) Destroy(gameObject);
        }

        private IEnumerator Start()
        {
            if (!_isInjected)
            {
                string errorMessage =
                    "[Convai SDK Setup Error] ConvaiRoomManager dependencies were not injected before Start().\n\n" +
                    "ConvaiRoomManager requires ConvaiManager setup to inject its dependencies.\n\n" +
                    "To fix this:\n" +
                    "1. Add ConvaiManager to your scene:\n" +
                    "   → Use menu: GameObject > Convai > Setup Required Components\n\n" +
                    "2. Verify ConvaiManager is active/enabled in the scene.\n\n" +
                    "📖 See: https://docs.convai.com/unity/quickstart";

                ConvaiLogger.Error(errorMessage, LogCategory.SDK);
                enabled = false;
                yield break;
            }

            ConvaiLogger.Debug("[ConvaiRoomManager] *** Initializing room connection... ***", LogCategory.SDK);

            var settings = ConvaiSettings.Instance;
            if (settings == null || !settings.HasApiKey)
            {
                ConvaiLogger.Error(
                    "Convai Settings not configured. Please set your API key in Edit > Project Settings > Convai SDK",
                    LogCategory.SDK);

                // Publish SessionError for missing API key
                _eventHub?.Publish(SessionError.Create(SessionErrorCodes.ConfigApiKeyMissing,
                    "API key not configured in ConvaiSettings"));

                HandleRoomConnectionFailed();
                yield break;
            }

            EnsureRuntimeSettingsDependencies();

            Player = _sceneDiscovery.DiscoverPlayer();
            if (Player == null)
            {
                HandleRoomConnectionFailed();
                yield break;
            }

            Player.OnTextMessageSent += HandlePlayerTextMessage;

            _characterRegistry = new CharacterRegistryAdapter(
                _sceneDiscovery.CharacterToParticipantMap,
                new List<IConvaiCharacterAgent>());

            _characterList = _sceneDiscovery.DiscoverCharacters(_characterRegistry);
            if (_characterList.Count == 0)
            {
                HandleRoomConnectionFailed();
                yield break;
            }

            ConvaiLogger.Debug(
                $"[ConvaiRoomManager] Discovered {CharacterList.Count} Character(s), connecting to first Character...",
                LogCategory.SDK);

            _playerSession = new PlayerSessionAdapter(Player, _eventHub);
            _configProvider = new ConfigurationProviderAdapter(settings)
            {
                ConnectionType = ConnectionType,
                VideoTrackName = ResolveVideoTrackName(),
                LlmProvider = LLMProvider,
                ServerEndpoint = ServerEndpoint,
                LipSyncTransportOptions = ResolveLipSyncTransportOptions()
            };
            WarnIfVisionComponentsMissing();
            _dispatcher = new MainThreadDispatcherAdapter(UnityScheduler.Post);

            // Create room controller using the platform-appropriate factory
            IConvaiRoomControllerFactory controllerFactory = _controllerFactory;
            if (controllerFactory == null)
            {
                ConvaiLogger.Error(
                    "[ConvaiRoomManager] IConvaiRoomControllerFactory not registered. " +
                    "Platform networking services were not composed before room startup. " +
                    "Ensure ConvaiServiceBootstrap completed successfully and that the current platform networking assembly was preserved in the player build.",
                    LogCategory.SDK);
                yield break;
            }

            _convaiRoomController = controllerFactory.Create(
                _characterRegistry,
                _playerSession,
                _configProvider,
                _dispatcher,
                _logger,
                _eventHub,
                _sectionNameResolver);

            if (_convaiRoomController == null)
            {
                ConvaiLogger.Error("[ConvaiRoomManager] Failed to create room controller.", LogCategory.SDK);
                yield break;
            }

            ConvaiLogger.Debug("[ConvaiRoomManager] Room controller created via factory.", LogCategory.SDK);

            _convaiRoomController.SetAudioSubscriptionPolicy(_remoteAudioPreferences.ShouldSubscribe);

            AudioSource audioSourceResolver(string characterId) =>
                _characterRegistry.TryGetAudioSource(characterId, out AudioSource source) ? source : null;

            _audioTrackManager?.Dispose();
            _container.TryGet(out IAudioStreamFactory audioStreamFactory);
            _audioTrackManager = new AudioTrackManager(
                () => CurrentRoom,
                _characterRegistry,
                _logger,
                audioSourceResolver,
                audioStreamFactory: audioStreamFactory,
                eventHub: _eventHub);
            _audioTrackManager.OnMicMuteChanged += HandleMicMuteChanged;

            _convaiRoomController.OnRoomConnectionSuccessful += HandleRoomConnectionSuccessful;
            _convaiRoomController.OnRoomConnectionFailed += HandleRoomConnectionFailed;
            _convaiRoomController.OnMicMuteChanged += HandleMicMuteChanged;
            _convaiRoomController.OnRoomReconnecting += HandleRoomReconnecting;
            _convaiRoomController.OnRoomReconnected += HandleRoomReconnected;
            _convaiRoomController.OnUnexpectedRoomDisconnected += HandleUnexpectedRoomDisconnected;
            _convaiRoomController.OnRemoteAudioTrackSubscribed += HandleRemoteAudioTrackSubscribed;
            _convaiRoomController.OnRemoteAudioTrackUnsubscribed += HandleRemoteAudioTrackUnsubscribed;

            EnsureAudioManager();

            SignalStartCompleted();

            if (!ConnectOnStart)
            {
                ConvaiLogger.Debug("[ConvaiRoomManager] Lazy connection mode - waiting for manual ConnectAsync() call",
                    LogCategory.SDK);
                yield break;
            }

            ConvaiLogger.Debug("[ConvaiRoomManager] Initialization complete. Auto-connecting...", LogCategory.SDK);
            _ = ConnectAsync().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    ConvaiLogger.Error(
                        $"[ConvaiRoomManager] Auto-connect failed: {task.Exception?.GetBaseException()?.Message}",
                        LogCategory.SDK);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OnDestroy()
        {
            if (Player != null) Player.OnTextMessageSent -= HandlePlayerTextMessage;

            if (_characterReadyToken != default && _eventHub != null)
            {
                _eventHub.Unsubscribe(_characterReadyToken);
                _characterReadyToken = default;
            }

            if (_sessionStateMachine != null)
                _sessionStateMachine.StateChanged -= OnSessionStateMachineStateChanged;

            if (_remoteAudioPreferences != null)
                _remoteAudioPreferences.RemoteAudioEnabledChanged -= OnRemoteAudioPreferenceChanged;

            _audioTrackManager?.Dispose();
            _convaiRoomController?.Dispose();
            _playerSession?.Dispose();
        }

        #endregion

        #region IConvaiRoomConnectionService Methods

        /// <inheritdoc />
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (!isActiveAndEnabled)
            {
                ConvaiLogger.Warning("[ConvaiRoomManager] ConnectAsync called but component is disabled.",
                    LogCategory.SDK);
                return false;
            }

            if (IsConnected) return true;

            if (CurrentState == SessionState.Error)
            {
                _logger?.Warning("[ConvaiRoomManager] ConnectAsync called but room is in error state.", LogCategory.SDK);
                return false;
            }

            if (CurrentState == SessionState.Disconnected)
            {
                if (!_hasStartedOnce)
                {
                    _logger?.Info("[ConvaiRoomManager] ConnectAsync waiting for Start() to complete...",
                        LogCategory.SDK);

                    int timeoutMs = _reconnectPolicy.StartWaitTimeoutMs;
                    using var timeoutCts = new CancellationTokenSource(timeoutMs);
                    using var linkedCts =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                    try
                    {
                        await Task.WhenAny(_startCompletedTcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));
                    }
                    catch (OperationCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return false;
                    }

                    if (!_hasStartedOnce)
                    {
                        _logger?.Error("[ConvaiRoomManager] ConnectAsync timed out waiting for Start().",
                            LogCategory.SDK);
                        return false;
                    }
                }

                if (_convaiRoomController == null)
                {
                    _logger?.Error("[ConvaiRoomManager] Cannot connect: controller not initialized.", LogCategory.SDK);
                    return false;
                }

                if (_connectTask is { IsCompleted: false }) return await _connectTask;

                UpdateSessionState(SessionState.Connecting);
                _connectTask = ConnectInternalAsync(cancellationToken);
                return await _connectTask;
            }

            if (CurrentState == SessionState.Connecting)
            {
                _logger?.Info("[ConvaiRoomManager] ConnectAsync waiting for connection to complete...",
                    LogCategory.SDK);

                TaskCompletionSource<bool> tcs = GetOrCreateConnectionStateTcs();

                try
                {
                    await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cancellationToken));
                }
                catch (OperationCanceledException)
                {
                }

                if (cancellationToken.IsCancellationRequested) return false;

                return tcs.Task.IsCompletedSuccessfully && tcs.Task.Result;
            }

            if (CurrentState == SessionState.Disconnecting)
            {
                _logger?.Warning("[ConvaiRoomManager] ConnectAsync called while disconnecting.", LogCategory.SDK);
                return false;
            }

            return IsConnected;
        }

        /// <inheritdoc />
        public async Task DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
            CancellationToken cancellationToken = default)
        {
            if (_audioTrackManager != null)
            {
                try
                {
                    await _audioTrackManager.UnpublishMicrophoneAsync();
                }
                catch (Exception ex)
                {
                    ConvaiLogger.Warning(
                        $"[ConvaiRoomManager] DisconnectAsync failed to unpublish microphone cleanly: {ex.Message}",
                        LogCategory.SDK);
                }
            }

            _audioTrackManager?.ClearState();

            _logger?.Info($"[ConvaiRoomManager] DisconnectAsync called with reason: {reason}", LogCategory.SDK);

            UpdateSessionState(SessionState.Disconnecting);

            if (_convaiRoomController != null) await _convaiRoomController.DisconnectFromRoomAsync(cancellationToken);

            CompleteDisconnectionTracking(true, "Disconnected from room");
        }

        /// <summary>
        ///     Disconnects from the Convai room synchronously (fire-and-forget).
        /// </summary>
        public void DisconnectFromRoom()
        {
            DisconnectAsync().ContinueWith(
                static t => ConvaiLogger.Error(
                    $"[ConvaiRoomManager] DisconnectAsync failed: {t.Exception?.GetBaseException().Message}",
                    LogCategory.SDK),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        #endregion

        #region IConvaiRoomAudioService Methods

        /// <summary>Sets whether the local microphone is muted.</summary>
        /// <param name="mute">True to mute; false to unmute.</param>
        public void SetMicMuted(bool mute)
        {
            _logger?.Debug($"[ConvaiRoomManager] SetMicMuted called: mute={mute}");
            if (_audioTrackManager != null) _audioTrackManager.SetMicMuted(mute);
            else if (_audioManager != null) _audioManager.SetMicMuted(mute);
            else _convaiRoomController?.SetMicMuted(mute);
        }

        /// <summary>Toggles the local microphone mute state.</summary>
        public bool ToggleMicMute()
        {
            bool newState = !IsMicMuted;
            SetMicMuted(newState);
            return newState;
        }

        /// <summary>Starts capturing and publishing microphone audio.</summary>
        /// <param name="microphoneIndex">Microphone device index.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task StartListeningAsync(int microphoneIndex = 0, CancellationToken cancellationToken = default)
        {
            if (RequiresUserGestureForAudio && !IsAudioPlaybackActive)
            {
                ConvaiLogger.Warning(
                    "[ConvaiRoomManager] StartListeningAsync aborted: call EnableAudioPlayback() from a user gesture before enabling microphone on this platform.",
                    LogCategory.SDK);
                _eventHub?.Publish(SessionError.Create(SessionErrorCodes.AudioMicPermissionDenied,
                    "User gesture required for microphone on this platform", null, true));
                return;
            }

            if (_audioTrackManager == null || !(_convaiRoomController?.IsConnectedToRoom ?? false))
            {
                ConvaiLogger.Warning(
                    "[ConvaiRoomManager] StartListeningAsync aborted: AudioTrackManager or Room not initialized.",
                    LogCategory.SDK);
                return;
            }

            if (_microphoneSourceFactory == null) _container?.TryGet(out _microphoneSourceFactory);

            IMicrophoneSourceFactory microphoneFactory = _microphoneSourceFactory;
            if (microphoneFactory == null)
            {
                ConvaiLogger.Error("[ConvaiRoomManager] StartListeningAsync: IMicrophoneSourceFactory not registered.",
                    LogCategory.SDK);
                return;
            }

            string[] devices = microphoneFactory.GetAvailableDevices() ?? Array.Empty<string>();
            if (devices.Length == 0)
            {
                ConvaiLogger.Warning("[ConvaiRoomManager] StartListeningAsync: No microphone devices detected.",
                    LogCategory.SDK);
                _eventHub?.Publish(SessionError.Create(SessionErrorCodes.AudioMicUnavailable,
                    "No microphone devices detected", null, true));
            }

            int deviceIndex = devices.Length == 0
                ? 0
                : Mathf.Clamp(microphoneIndex, 0, devices.Length - 1);
            string deviceName = devices.Length == 0 ? null : devices[deviceIndex];

            IMicrophoneSource microphoneSource = microphoneFactory.Create(deviceName, deviceIndex, gameObject);
            var publishOptions = AudioPublishOptions.DefaultMicrophone;

            bool published = await _audioTrackManager.PublishMicrophoneAsync(microphoneSource, publishOptions);
            if (!published)
            {
                ConvaiLogger.Error("[ConvaiRoomManager] StartListeningAsync: failed to publish microphone.",
                    LogCategory.SDK);
                _eventHub?.Publish(SessionError.Create(SessionErrorCodes.AudioMicPublishFailed,
                    "Failed to publish microphone audio", null, true));
            }
        }

        /// <summary>Stops capturing and publishing microphone audio.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task StopListeningAsync(CancellationToken cancellationToken = default)
        {
            if (_audioTrackManager != null) await _audioTrackManager.UnpublishMicrophoneAsync();
        }

        /// <inheritdoc />
        public bool SetCharacterMuted(string characterId, bool muted)
        {
            if (string.IsNullOrEmpty(characterId)) return false;
            if (_audioTrackManager != null)
            {
                _audioTrackManager.SetCharacterAudioMuted(characterId, muted);
                return true;
            }

            if (_audioManager != null)
            {
                _audioManager.MuteCharacter(characterId, muted);
                return true;
            }

            return _convaiRoomController?.SetCharacterAudioMuted(characterId, muted) ?? false;
        }

        /// <inheritdoc />
        public bool IsCharacterMuted(string characterId)
        {
            return !string.IsNullOrEmpty(characterId) &&
                   (_audioTrackManager?.IsCharacterAudioMuted(characterId) ??
                    _convaiRoomController?.IsCharacterAudioMuted(characterId) ?? false);
        }

        /// <inheritdoc />
        public bool SetRemoteAudioEnabled(string characterId, bool enabled)
        {
            bool result = _remoteAudioPreferences?.SetRemoteAudioEnabled(characterId, enabled) ?? false;

            if (result && _convaiRoomController != null)
                _convaiRoomController.ApplyRemoteAudioPreference(characterId, enabled);

            return result;
        }

        /// <inheritdoc />
        public bool IsRemoteAudioEnabled(string characterId) =>
            _remoteAudioPreferences?.IsRemoteAudioEnabled(characterId) ?? false;

        #endregion

        #region Character Audio Convenience Methods

        /// <summary>Sets whether the given character's audio is muted.</summary>
        /// <param name="character">Character agent.</param>
        /// <param name="mute">True to mute; false to unmute.</param>
        /// <returns>True when the state is applied; otherwise false.</returns>
        public bool SetCharacterAudioMuted(IConvaiCharacterAgent character, bool mute) =>
            character != null && SetCharacterMuted(character.CharacterId, mute);

        /// <summary>Mutes the given character's audio.</summary>
        /// <param name="character">Character agent.</param>
        /// <returns>True when the state is applied; otherwise false.</returns>
        public bool MuteCharacter(IConvaiCharacterAgent character) => SetCharacterAudioMuted(character, true);

        /// <summary>Unmutes the given character's audio.</summary>
        /// <param name="character">Character agent.</param>
        /// <returns>True when the state is applied; otherwise false.</returns>
        public bool UnmuteCharacter(IConvaiCharacterAgent character) => SetCharacterAudioMuted(character, false);

        /// <summary>Gets whether the given character's audio is muted.</summary>
        /// <param name="character">Character agent.</param>
        /// <returns>True if muted; otherwise false.</returns>
        public bool IsCharacterAudioMuted(IConvaiCharacterAgent character) =>
            character != null && IsCharacterMuted(character.CharacterId);

        #endregion

        #region Private Methods

        private void EnsureAudioManager()
        {
            if (_audioManager != null || _convaiRoomController == null ||
                _sceneDiscovery?.CharacterToParticipantMap == null) return;

            try
            {
                _audioManager = new DefaultAudioManager(this, this, _logger);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiRoomManager] Failed to create DefaultAudioManager: {ex.Message}",
                    LogCategory.SDK);
            }
        }

        // WebGL bootstrap recovery methods are defined in ConvaiRoomManager.WebGL.cs

        private void EnsureRuntimeSettingsDependencies()
        {
            if (_container == null) return;

            if (_runtimeSettingsService == null) _container.TryGet(out _runtimeSettingsService);

            if (_microphoneDeviceService == null) _container.TryGet(out _microphoneDeviceService);
        }

        private void WarnIfVisionComponentsMissing()
        {
            if (ConnectionType != ConvaiConnectionType.Video) return;

            (bool hasPublisher, bool hasFrameSource) = GetVisionComponentFlags();
            if (hasPublisher && hasFrameSource) return;

            ConvaiLogger.Warning(
                "[ConvaiRoomManager] ConnectionType is Video but vision components are missing. Add ConvaiVideoPublisher and CameraVisionFrameSource (or another IVisionFrameSource) under this RoomManager to enable vision streaming.",
                LogCategory.SDK);
        }

        private (bool hasPublisher, bool hasFrameSource) GetVisionComponentFlags()
        {
            bool hasPublisher = false;
            bool hasFrameSource = false;

            foreach (MonoBehaviour component in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!hasPublisher && component is IVideoPublisher) hasPublisher = true;

                if (!hasFrameSource && component is IVisionFrameSource) hasFrameSource = true;

                if (hasPublisher && hasFrameSource) break;
            }

            return (hasPublisher, hasFrameSource);
        }

        private LipSyncTransportOptions ResolveLipSyncTransportOptions()
        {
            if (CharacterList == null || CharacterList.Count == 0)
            {
                ConvaiLogger.Debug(
                    "[ConvaiRoomManager] No characters available for lip sync capability discovery. Lip sync transport remains disabled.",
                    LogCategory.LipSync);
                return LipSyncTransportOptions.Disabled;
            }

            var resolvedOptions = new List<LipSyncTransportOptions>(CharacterList.Count);
            var resolvedSources = new List<string>(CharacterList.Count);

            foreach (IConvaiCharacterAgent characterAgent in CharacterList)
            {
                if (characterAgent is not MonoBehaviour characterMono) continue;

                ILipSyncCapabilitySource source = characterMono.GetComponent<ILipSyncCapabilitySource>() ??
                                                  characterMono.GetComponentInChildren<ILipSyncCapabilitySource>(true);

                if (source == null) continue;

                if (!source.TryGetLipSyncTransportOptions(out LipSyncTransportOptions options) ||
                    !options.IsValid) continue;

                resolvedOptions.Add(options);
                resolvedSources.Add(characterMono.name);
            }

            if (resolvedOptions.Count == 0)
            {
                ConvaiLogger.Debug(
                    "[ConvaiRoomManager] No valid lip sync capability source found. Lip sync transport remains disabled.",
                    LogCategory.LipSync);
                return LipSyncTransportOptions.Disabled;
            }

            var uniqueOptions = new Dictionary<string, LipSyncTransportOptions>(StringComparer.Ordinal);
            for (int i = 0; i < resolvedOptions.Count; i++)
            {
                LipSyncTransportOptions option = resolvedOptions[i];
                uniqueOptions[BuildLipSyncContractKey(option)] = option;
            }

            if (uniqueOptions.Count > 1)
            {
                string joinedSources = string.Join(", ", resolvedSources);
                ConvaiLogger.Error(
                    $"[ConvaiRoomManager] Conflicting lip sync contracts detected across characters ({joinedSources}). Lip sync transport has been disabled for this room.",
                    LogCategory.LipSync);
                _eventHub?.Publish(SessionError.Create(
                    "lipsync.contract_conflict",
                    "Multiple characters advertise conflicting lip sync transport contracts in a single room."));
                return LipSyncTransportOptions.Disabled;
            }

            LipSyncTransportOptions selected = default;
            foreach (KeyValuePair<string, LipSyncTransportOptions> pair in uniqueOptions)
            {
                selected = pair.Value;
                break;
            }

            ConvaiLogger.Info(
                $"[ConvaiRoomManager] Lip sync transport resolved: provider={selected.Provider}, format={selected.Format}, profileId={selected.ProfileId}, fps={selected.OutputFps}, chunk={selected.ChunkSize}, sourceCount={selected.SourceBlendshapeNames.Count}",
                LogCategory.LipSync);
            return selected;
        }

        private static string BuildLipSyncContractKey(LipSyncTransportOptions options)
        {
            string sourceKey = options.SourceBlendshapeNames == null
                ? string.Empty
                : string.Join("|", options.SourceBlendshapeNames);

            return string.Concat(
                options.Provider, "::",
                options.Format, "::",
                options.ProfileId.Value, "::",
                options.EnableChunking ? "1" : "0", "::",
                options.ChunkSize.ToString(), "::",
                options.OutputFps.ToString(), "::",
                sourceKey);
        }

        private string ResolveVideoTrackName()
        {
            foreach (MonoBehaviour component in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component is IVideoPublisher publisher &&
                    !string.IsNullOrWhiteSpace(publisher.VideoTrackName))
                    return publisher.VideoTrackName;
            }

            return VideoPublishOptions.Default.TrackName;
        }

        private void HandleRoomConnectionSuccessful()
        {
            ConvaiLogger.Info("[ConvaiRoomManager] *** Room connection successful! ***", LogCategory.SDK);

            RecordConnectionSuccess(
                _convaiRoomController?.RoomName,
                _convaiRoomController?.CharacterSessionID,
                _convaiRoomController?.SessionID,
                CharacterList?.Count > 0 ? CharacterList[0]?.CharacterId : null);

            StartCoroutine(AutoStartMicrophoneCoroutine());
        }

        private void HandleRoomConnectionFailed()
        {
            ConvaiLogger.Error("[ConvaiRoomManager] *** Room connection FAILED! ***", LogCategory.SDK);

            RecordConnectionFailure(SessionErrorCodes.ConnectionFailed,
                "Failed to connect to Convai room");
        }

        private void HandleMicMuteChanged(bool isMuted)
        {
            _eventHub?.Publish(Domain.DomainEvents.Runtime.MicMuteChanged.Create(isMuted));
            MicMuteChanged?.Invoke(isMuted);
        }

        private void HandleRoomReconnecting()
        {
            ConvaiLogger.Info("[ConvaiRoomManager] Room is reconnecting...", LogCategory.SDK);
            UpdateSessionState(SessionState.Reconnecting);
        }

        private void HandleRoomReconnected()
        {
            ConvaiLogger.Info("[ConvaiRoomManager] Room reconnected successfully!", LogCategory.SDK);
            Connected?.Invoke();
        }

        private void HandleUnexpectedRoomDisconnected()
        {
            ConvaiLogger.Info(
                "[ConvaiRoomManager] Room disconnected unexpectedly; clearing runtime media/session state.",
                LogCategory.SDK);
            _audioTrackManager?.SetMicMuted(false);
            _audioTrackManager?.ClearState();
            CompleteDisconnectionTracking(CurrentState != SessionState.Disconnected,
                "Handled unexpected room disconnect");
        }

        private void HandlePlayerTextMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                ConvaiLogger.Warning("[ConvaiRoomManager] HandlePlayerTextMessage received empty text; ignoring.",
                    LogCategory.SDK);
                return;
            }

            if (!IsConnected)
            {
                ConvaiLogger.Warning(
                    "[ConvaiRoomManager] HandlePlayerTextMessage called while not connected; message dropped.",
                    LogCategory.SDK);
                return;
            }

            if (RtvHandler == null)
            {
                ConvaiLogger.Warning(
                    "[ConvaiRoomManager] HandlePlayerTextMessage: RtvHandler is null; message dropped.",
                    LogCategory.SDK);
                return;
            }

            var message = new RTVIUserTextMessage(text);
            RtvHandler.SendData(message);
            ConvaiLogger.Debug($"[ConvaiRoomManager] Sent user text message: {text}", LogCategory.SDK);
        }

        private void HandleRemoteAudioTrackSubscribed(IRemoteAudioTrack audioTrack, string participantSid,
            string characterId)
        {
            // Track is already wrapped in the platform-agnostic abstraction by the adapter/controller
            _audioTrackManager?.HandleRemoteAudioTrackSubscribed(audioTrack, participantSid, characterId);
        }

        private void HandleRemoteAudioTrackUnsubscribed(string participantSid, string characterId) =>
            _audioTrackManager?.HandleRemoteAudioTrackUnsubscribed(participantSid);

        private IEnumerator AutoStartMicrophoneCoroutine()
        {
            yield return new WaitForSeconds(_reconnectPolicy.AutoMicStartDelaySeconds);

            if (RequiresUserGestureForAudio && !IsAudioPlaybackActive)
            {
                ConvaiLogger.Debug(
                    "[ConvaiRoomManager] Skipping auto-start microphone: platform requires a user gesture first. " +
                    "Call EnableAudioAndStartListening() from a UI button.", LogCategory.SDK);
                yield break;
            }

            if (IsConnected)
            {
                EnsureRuntimeSettingsDependencies();

                int microphoneIndex = 0;
                if (_runtimeSettingsService != null && _microphoneDeviceService != null)
                {
                    string preferredDeviceId = _runtimeSettingsService.Current.PreferredMicrophoneDeviceId;
                    int resolvedIndex = _microphoneDeviceService.ResolvePreferredDeviceIndex(preferredDeviceId);
                    if (resolvedIndex >= 0) microphoneIndex = resolvedIndex;
                }

                StartListeningAsync(microphoneIndex).ContinueWith(
                    static t => ConvaiLogger.Error(
                        $"[ConvaiRoomManager] StartListeningAsync failed: {t.Exception?.GetBaseException().Message}",
                        LogCategory.SDK),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        private void SignalStartCompleted()
        {
            _hasStartedOnce = true;
            _startCompletedTcs.TrySetResult(true);
        }

        private void UpdateSessionState(SessionState newState, string errorCode = null)
        {
            if (_sessionStateMachine == null)
            {
                _logger?.Warning("[ConvaiRoomManager] Session state machine not initialized; skipping state update.",
                    LogCategory.SDK);
                return;
            }

            string sessionId = _convaiRoomController?.SessionID;

            if (!_sessionStateMachine.TryTransition(newState, sessionId, errorCode))
            {
                _logger?.Warning($"[ConvaiRoomManager] Invalid transition to {newState}; forcing transition.",
                    LogCategory.SDK);
                _sessionStateMachine.ForceTransition(newState, sessionId, errorCode);
            }
        }

        private async Task<bool> ConnectInternalAsync(CancellationToken cancellationToken)
        {
            try
            {
                IConvaiCharacterAgent activeCharacter = CharacterList?.Count > 0 ? CharacterList[0] : null;
                if (activeCharacter == null)
                {
                    _logger?.Error("[ConvaiRoomManager] Cannot connect: no active character.", LogCategory.SDK);
                    UpdateSessionState(SessionState.Error);
                    return false;
                }

                string characterId = activeCharacter.CharacterId;
                string characterName = activeCharacter.CharacterName;
                bool enableSessionResume = activeCharacter.EnableSessionResume;

                ResumePolicy effectiveResumePolicy = enableSessionResume
                    ? _reconnectPolicy.ResumePolicy
                    : ResumePolicy.AlwaysFresh;

                RoomJoinOptions joinOptions = RoomJoinOptions.FromContext(_connectionContext, _reconnectPolicy);

                if (effectiveResumePolicy == ResumePolicy.AlwaysFresh)
                {
                    joinOptions = joinOptions.IsJoinRequest
                        ? new RoomJoinOptions(joinOptions.RoomName, null, joinOptions.SpawnAgent,
                            joinOptions.MaxNumParticipants)
                        : RoomJoinOptions.CreateNew(null, joinOptions.MaxNumParticipants);
                }

                string reconnectMode = joinOptions.IsJoinRequest ? "rejoin" : "create";
                _logger?.Info($"[ConvaiRoomManager] Attempting {reconnectMode} for character: {characterName}",
                    LogCategory.SDK);

                return await ConnectWithRetryAsync(characterId, enableSessionResume, joinOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.Error($"[ConvaiRoomManager] ConnectInternalAsync failed: {ex.Message}", LogCategory.SDK);
                UpdateSessionState(SessionState.Error);
                return false;
            }
        }

        private async Task<bool> ConnectWithRetryAsync(
            string characterId,
            bool enableSessionResume,
            RoomJoinOptions joinOptions,
            CancellationToken cancellationToken)
        {
            if (_convaiRoomController == null)
                throw new InvalidOperationException("Connection controller has not been initialized.");

            string sessionId = joinOptions?.CharacterSessionId;
            if (string.IsNullOrEmpty(sessionId) && enableSessionResume)
                sessionId = _sessionPersistence?.LoadSession(characterId);

            for (int attempt = 0; attempt < _defaultRetryDelays.Length; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string mode = joinOptions?.IsJoinRequest == true ? "join" : "create";
                _logger?.Info(
                    $"[ConvaiRoomManager] Attempt {attempt + 1} connecting character {characterId} (mode={mode})",
                    LogCategory.SDK);

                bool connected = await _convaiRoomController.InitializeAsync(
                    ConnectionType.ToApiString(),
                    LLMProvider.ToApiString(),
                    CoreServerURL,
                    characterId,
                    sessionId,
                    enableSessionResume,
                    joinOptions,
                    cancellationToken);

                if (connected)
                {
                    PersistSession(characterId, enableSessionResume);
                    _logger?.Info(
                        $"[ConvaiRoomManager] Character {characterId} connected successfully (mode={mode}).",
                        LogCategory.SDK);
                    return true;
                }

                if (joinOptions?.IsJoinRequest == true && attempt == 0)
                {
                    _logger?.Info(
                        $"[ConvaiRoomManager] Join failed for room {joinOptions.RoomName}, falling back to create mode.",
                        LogCategory.SDK);
                    joinOptions = RoomJoinOptions.CreateNew(sessionId, joinOptions.MaxNumParticipants);
                }

                if (attempt >= _defaultRetryDelays.Length - 1) break;

                TimeSpan delay = _defaultRetryDelays[attempt + 1];
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);

                sessionId = null;
            }

            _logger?.Error(
                $"[ConvaiRoomManager] Failed to connect character {characterId} after {_defaultRetryDelays.Length} attempts.",
                LogCategory.SDK);
            return false;
        }

        private void PersistSession(string characterId, bool enableSessionResume)
        {
            if (!enableSessionResume || _sessionPersistence == null) return;

            string sessionId = _convaiRoomController?.CharacterSessionID;
            if (!string.IsNullOrEmpty(sessionId)) _sessionPersistence.SaveSession(characterId, sessionId);
        }

        private void RecordConnectionSuccess(string roomName, string characterSessionId, string sessionId,
            string characterId)
        {
            _connectionContext = new ConnectionContext(
                roomName,
                characterSessionId,
                sessionId,
                characterId,
                DateTime.UtcNow);

            if (_sessionService != null)
            {
                _sessionService.SetActiveSession(characterId, sessionId);

                IConvaiCharacterAgent activeCharacter = CharacterList?.Count > 0 ? CharacterList[0] : null;
                bool enableSessionResume = activeCharacter?.EnableSessionResume ?? false;
                if (enableSessionResume && !string.IsNullOrEmpty(characterSessionId))
                    _sessionService.StoreSession(characterId, characterSessionId);
            }

            Connected?.Invoke();
        }

        private void RecordConnectionFailure(string errorCode, string errorMessage)
        {
            UpdateSessionState(SessionState.Error, errorCode);
            _eventHub?.Publish(SessionError.Create(errorCode, errorMessage, _convaiRoomController?.SessionID));
            ConnectionFailed?.Invoke();
        }

        private void CompleteDisconnectionTracking(bool updateSessionState, string completionMessage)
        {
            if (updateSessionState) UpdateSessionState(SessionState.Disconnected);

            if (_connectionContext.HasValidRoom)
                _connectionContext = _connectionContext.WithDisconnection(DateTime.UtcNow);

            _sessionService?.ClearActiveSession();

            _logger?.Info($"[ConvaiRoomManager] {completionMessage}", LogCategory.SDK);
        }

        private void OnSessionStateMachineStateChanged(SessionStateChanged stateChanged)
        {
            OnSessionStateChanged?.Invoke(stateChanged);
            SignalConnectionStateWaiters(stateChanged.NewState);
        }

        private void SignalConnectionStateWaiters(SessionState newState)
        {
            lock (_connectionStateTcsLock)
            {
                if (_connectionStateTcs == null || _connectionStateTcs.Task.IsCompleted)
                    return;

                switch (newState)
                {
                    case SessionState.Connected:
                        _connectionStateTcs.TrySetResult(true);
                        break;
                    case SessionState.Error:
                    case SessionState.Disconnected:
                        _connectionStateTcs.TrySetResult(false);
                        break;
                }
            }
        }

        private TaskCompletionSource<bool> GetOrCreateConnectionStateTcs()
        {
            lock (_connectionStateTcsLock)
            {
                if (_connectionStateTcs == null || _connectionStateTcs.Task.IsCompleted)
                    _connectionStateTcs = new TaskCompletionSource<bool>();
                return _connectionStateTcs;
            }
        }

        private void HandleCharacterReadyEvent(CharacterReady readyEvent)
        {
            SessionState currentState = CurrentState;
            if (currentState != SessionState.Connecting && currentState != SessionState.Reconnecting) return;

            string activeCharacterId = CharacterList?.Count > 0 ? CharacterList[0]?.CharacterId : null;

            if (!string.IsNullOrEmpty(activeCharacterId))
            {
                bool matchesByCharacterId = !string.IsNullOrEmpty(readyEvent.CharacterId) &&
                                            string.Equals(activeCharacterId, readyEvent.CharacterId,
                                                StringComparison.OrdinalIgnoreCase);

                bool matchesByParticipantId = false;
                if (!string.IsNullOrEmpty(readyEvent.ParticipantId) &&
                    _characterRegistry != null &&
                    _characterRegistry.TryGetCharacter(activeCharacterId, out CharacterDescriptor activeDescriptor))
                {
                    if (!string.IsNullOrEmpty(activeDescriptor.ParticipantId))
                    {
                        matchesByParticipantId = string.Equals(activeDescriptor.ParticipantId, readyEvent.ParticipantId,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        if (CharacterList != null && CharacterList.Count == 1)
                        {
                            _characterRegistry.RegisterCharacter(
                                activeDescriptor.WithParticipantId(readyEvent.ParticipantId));
                            matchesByParticipantId = true;
                        }
                    }
                }

                // If neither match applies, ignore (likely another character in multi-bot rooms).
                if (!matchesByCharacterId && !matchesByParticipantId) return;
            }

            UpdateSessionState(SessionState.Connected);
        }

        private void OnRemoteAudioPreferenceChanged(string characterId, bool enabled) =>
            RemoteAudioEnabledChanged?.Invoke(characterId, enabled);

        #endregion
    }
}
