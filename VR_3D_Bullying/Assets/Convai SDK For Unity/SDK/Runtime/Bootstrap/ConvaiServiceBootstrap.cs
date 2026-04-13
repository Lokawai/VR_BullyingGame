using System;
using Convai.Application;
using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Identity;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Bootstrap;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Networking.Services;
using Convai.Infrastructure.Persistence;
using Convai.Runtime.Adapters;
using Convai.Runtime.Components;
using Convai.Runtime.Identity;
using Convai.Runtime.Logging;
using Convai.Runtime.Persistence;
using Convai.Runtime.Presentation.Services;
using Convai.Runtime.Room;
using Convai.Runtime.Services;
using Convai.Runtime.Services.CharacterLocator;
using Convai.Runtime.Settings;
using Convai.Shared.Abstractions;
using Convai.Shared.DependencyInjection;
using Convai.Shared.Interfaces;
using UnityEngine;
using UnityEngine.Serialization;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime
{
    /// <summary>
    ///     Bootstraps the Convai SDK by initializing the service locator and registering core services.
    ///     Integrates DI container with Unity lifecycle.
    /// </summary>
    /// <remarks>
    ///     This MonoBehaviour should be attached to a GameObject in the first scene of your application.
    ///     It will:
    ///     - Initialize ConvaiServiceLocator
    ///     - Register core services (UnityScheduler, EventHub, Logger)
    ///     - Initialize all singleton services
    ///     - Persist across scene loads (DontDestroyOnLoad)
    ///     - Clean up on application quit
    ///     When executed outside play mode (e.g., EditMode tests) the bootstrap GameObject is hidden and
    ///     marked with <see cref="HideFlags.HideAndDontSave" /> so that editor sessions do not accumulate
    ///     duplicate instances.
    ///     Usage:
    ///     1. Create a GameObject in your first scene (e.g., "ConvaiBootstrap")
    ///     2. Attach this script to the GameObject
    ///     3. The script will automatically initialize on Awake()
    ///     After bootstrap, you can access services via:
    ///     - ConvaiServiceLocator.Get&lt;IUnityScheduler&gt;()
    ///     - ConvaiServiceLocator.Get&lt;IEventHub&gt;()
    ///     - ConvaiServiceLocator.Get&lt;ILogger&gt;()
    /// </remarks>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-1000)]
    internal class ConvaiServiceBootstrap : MonoBehaviour
    {
        private static ConvaiServiceBootstrap _instance;

        [Header("Bootstrap Settings")]
        [Tooltip("If true, this GameObject will persist across scene loads")]
        [FormerlySerializedAs("persistAcrossScenes")]
        [SerializeField]
        private bool _persistAcrossScenes = true;

        [Tooltip("If true, eagerly initialize all singleton services at startup")]
        [FormerlySerializedAs("eagerInitialization")]
        [SerializeField]
        private bool _eagerInitialization = true;

        [Tooltip("If true, enable debug logging for bootstrap process")]
        [FormerlySerializedAs("debugLogging")]
        [SerializeField]
        private bool _debugLogging;

        [Header("Optional Services")]
        [Tooltip(
            "If true, register the default IConvaiNotificationService. Disable if providing a custom implementation.")]
        [SerializeField]
        private bool _registerDefaultNotificationService = true;

        /// <summary>
        ///     Gets whether the Convai SDK has been bootstrapped.
        /// </summary>
        internal static bool IsBootstrapped { get; private set; }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Duplicate detected, destroying copy.",
                        LogCategory.Bootstrap);
                }

                if (UnityEngine.Application.isPlaying)
                    DestroyImmediate(gameObject);
                else
                    DestroyImmediate(gameObject);
                return;
            }

            _instance = this;

            if (_persistAcrossScenes)
            {
                if (transform.parent != null) transform.SetParent(null);
                if (UnityEngine.Application.isPlaying)
                    DontDestroyOnLoad(gameObject);
                else
                    gameObject.hideFlags = HideFlags.HideAndDontSave;
            }

            TryBootstrap();
        }

        private void Reset()
        {
            if (!UnityEngine.Application.isPlaying) TryBootstrap();
        }

        private void OnEnable()
        {
            if (!UnityEngine.Application.isPlaying) TryBootstrap();
        }

        private void OnDestroy()
        {
            if (_instance != this) return;

            if (IsBootstrapped) Shutdown();

            _instance = null;
        }

        private void OnApplicationQuit() => Shutdown();

        private void TryBootstrap()
        {
            if (IsBootstrapped)
            {
                if (ConvaiServiceLocator.IsInitialized)
                {
                    if (_debugLogging)
                    {
                        ConvaiLogger.Warning("[ConvaiServiceBootstrap] Already bootstrapped. Destroying duplicate.",
                            LogCategory.Bootstrap);
                    }

                    if (UnityEngine.Application.isPlaying)
                        Destroy(gameObject);
                    else
                        DestroyImmediate(gameObject);
                    return;
                }

                if (_debugLogging)
                {
                    ConvaiLogger.Warning(
                        "[ConvaiServiceBootstrap] Bootstrap flag set but services not initialized. Re-bootstrapping.",
                        LogCategory.Bootstrap);
                }

                IsBootstrapped = false;
            }

            Bootstrap();
        }

        /// <summary>
        ///     Bootstraps the Convai SDK by initializing services.
        /// </summary>
        private void Bootstrap()
        {
            if (_debugLogging)
                ConvaiLogger.Debug("[ConvaiServiceBootstrap] Starting bootstrap...", LogCategory.Bootstrap);

            try
            {
                ConvaiServiceLocator.Initialize();
                if (_debugLogging)
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Service locator initialized", LogCategory.Bootstrap);

                RegisterCoreServices();
                if (_debugLogging)
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Core services registered", LogCategory.Bootstrap);

                RegisterPlatformNetworkingServices();
                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Platform networking services registered",
                        LogCategory.Bootstrap);
                }

                if (_eagerInitialization)
                {
                    ConvaiServiceLocator.InitializeServices();
                    if (_debugLogging)
                    {
                        ConvaiLogger.Debug("[ConvaiServiceBootstrap] Singleton services initialized",
                            LogCategory.Bootstrap);
                    }
                }
                else
                    InitializeEssentialServices();

                InitializeConvaiRoomSessionApi();
                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] ConvaiRoomSession API initialized",
                        LogCategory.Bootstrap);
                }

                IsBootstrapped = true;

                if (_debugLogging)
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Bootstrap complete!", LogCategory.Bootstrap);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiServiceBootstrap] Bootstrap failed: {ex.Message}", LogCategory.Bootstrap);
                ConvaiLogger.Exception(ex, LogCategory.Bootstrap);
                throw;
            }
        }

        /// <summary>
        ///     Registers core Convai services with the service locator.
        /// </summary>
        private void RegisterCoreServices()
        {
            var scheduler = UnityScheduler.Instance;
            ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<IUnityScheduler>(scheduler));

            if (_debugLogging)
                ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered IUnityScheduler", LogCategory.Bootstrap);

            ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<IEventHub>(static container =>
            {
                var schedulerInstance = container.Get<IUnityScheduler>();
                _ = container.TryGet(out ILogger logger);
                var eventHub = new EventHub(schedulerInstance, logger);
                ConvaiLogger.Debug(
                    $"[ConvaiServiceBootstrap] EventHub SINGLETON created: instance {eventHub.GetHashCode()}",
                    LogCategory.Bootstrap);
                return eventHub;
            }));

            if (_debugLogging)
                ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered IEventHub", LogCategory.Bootstrap);

            if (!ConvaiServiceLocator.IsRegistered<IEndUserIdProvider>())
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IEndUserIdProvider, DeviceEndUserIdProvider>());

                if (_debugLogging)
                {
                    ConvaiLogger.Debug(
                        "[ConvaiServiceBootstrap] Registered IEndUserIdProvider (DeviceEndUserIdProvider).",
                        LogCategory.Bootstrap);
                }
            }

            ILogger loggerInstance = FindOrCreateLogger();
            if (loggerInstance != null)
            {
                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton(loggerInstance));
                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered ILogger (ConvaiLogger)",
                        LogCategory.Bootstrap);
                }

                var defaultSink = new UnityConsoleSink();
                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<ILogSink>(defaultSink));
                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered ILogSink (UnityConsoleSink)",
                        LogCategory.Bootstrap);
                }
            }
            else
            {
                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] No logger available, continuing without ILogger",
                        LogCategory.Bootstrap);
                }
            }

            RegisterRoomServiceAdapters();

            RegisterAudioManager();

            RegisterVisibleCharacterService();

            RegisterPlayerInputService();

            RegisterTranscriptUIServices();

            RegisterTranscriptUIController();

            RegisterRuntimeSettingsServices();

            RegisterSessionServices();

            RegisterNotificationServices();

            RegisterApplicationServices();
        }

        private void RegisterPlatformNetworkingServices()
        {
            PlatformNetworkingBootstrap.EnsureRegistered(_debugLogging);
        }

        /// <summary>
        ///     Initializes a small set of singleton services that must run to apply settings
        ///     and subscribe to events, even when full eager initialization is disabled.
        /// </summary>
        private void InitializeEssentialServices()
        {
            // These services have side effects (event subscriptions / applying current settings)
            // and are expected to be active during normal SDK usage.
            try
            {
                _ = ConvaiServiceLocator.Get<IConvaiRuntimeSettingsService>();
                _ = ConvaiServiceLocator.Get<RuntimeSettingsTranscriptApplier>();
                _ = ConvaiServiceLocator.Get<RuntimeSettingsPlayerIdentityApplier>();
                _ = ConvaiServiceLocator.Get<RuntimeSettingsAudioApplier>();
                _ = ConvaiServiceLocator.Get<ConvaiNotificationEventBridge>();

                if (ConvaiServiceLocator.IsRegistered<RuntimeSettingsNotificationApplier>())
                    _ = ConvaiServiceLocator.Get<RuntimeSettingsNotificationApplier>();

                if (_debugLogging)
                {
                    ConvaiLogger.Debug(
                        "[ConvaiServiceBootstrap] Essential services initialized (eagerInitialization=false)",
                        LogCategory.Bootstrap);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Warning($"[ConvaiServiceBootstrap] Failed to initialize essential services: {ex.Message}",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Registers the IVisibleCharacterService.
        ///     This service tracks which characters are currently visible to the player.
        /// </summary>
        private void RegisterVisibleCharacterService()
        {
            try
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IVisibleCharacterService>(static _ => new VisibleCharacterService()));

                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered IVisibleCharacterService",
                        LogCategory.Bootstrap);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error(
                    $"[ConvaiServiceBootstrap] Failed to register IVisibleCharacterService: {ex.Message}",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Registers the IPlayerInputService.
        ///     This service provides access to the player agent for UI components.
        /// </summary>
        private void RegisterPlayerInputService()
        {
            try
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IPlayerInputService>(static _ => new PlayerInputService()));

                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered IPlayerInputService",
                        LogCategory.Bootstrap);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiServiceBootstrap] Failed to register IPlayerInputService: {ex.Message}",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Registers the ITranscriptUIServices aggregate interface.
        ///     This simplifies DI for TranscriptUIBase components by bundling all required services.
        /// </summary>
        private void RegisterTranscriptUIServices()
        {
            try
            {
                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<ITranscriptUIServices>(static container =>
                {
                    var visibilityService = container.Get<IVisibleCharacterService>();
                    var characterLocator = container.Get<IConvaiCharacterLocatorService>();
                    var playerInput = container.Get<IPlayerInputService>();

                    return new TranscriptUIServices(
                        visibilityService,
                        characterLocator,
                        playerInput);
                }));

                if (_debugLogging)
                {
                    ConvaiLogger.Debug(
                        "[ConvaiServiceBootstrap] Registered ITranscriptUIServices (aggregate interface)",
                        LogCategory.Bootstrap);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiServiceBootstrap] Failed to register ITranscriptUIServices: {ex.Message}",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Registers the TranscriptUIController service.
        ///     This controller manages transcript UI instances and routes events to them.
        /// </summary>
        private void RegisterTranscriptUIController()
        {
            try
            {
                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton(container =>
                {
                    var eventHub = container.Get<IEventHub>();
                    var controller = new TranscriptUIController(eventHub);
                    if (_debugLogging)
                    {
                        ConvaiLogger.Debug(
                            $"[ConvaiServiceBootstrap] TranscriptUIController created with EventHub: {eventHub.GetHashCode()}",
                            LogCategory.Bootstrap);
                    }

                    return controller;
                }));

                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered TranscriptUIController",
                        LogCategory.Bootstrap);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiServiceBootstrap] Failed to register TranscriptUIController: {ex.Message}",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Registers runtime settings services and side-effect appliers.
        /// </summary>
        private void RegisterRuntimeSettingsServices()
        {
            try
            {
                if (!ConvaiServiceLocator.IsRegistered<IKeyValueStore>())
                {
                    ConvaiServiceLocator.Register(
                        ServiceDescriptor.Singleton<IKeyValueStore>(static _ => new PlayerPrefsKeyValueStore()));
                }

                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IMicrophoneDeviceService>(static _ => new MicrophoneDeviceService()));

                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<IConvaiRuntimeSettingsStore>(container =>
                {
                    var keyValueStore = container.Get<IKeyValueStore>();
                    return new ConvaiRuntimeSettingsStore(keyValueStore);
                }));

                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<IConvaiRuntimeSettingsService>(container =>
                {
                    var store = container.Get<IConvaiRuntimeSettingsStore>();
                    var microphoneDeviceService = container.Get<IMicrophoneDeviceService>();
                    return new ConvaiRuntimeSettingsService(ConvaiSettings.Instance, store, microphoneDeviceService);
                }));

                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IConvaiSettingsPanelController>(static _ =>
                        new ConvaiSettingsPanelController()));

                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton(container =>
                {
                    var settings = container.Get<IConvaiRuntimeSettingsService>();
                    var microphoneDeviceService = container.Get<IMicrophoneDeviceService>();
                    var scheduler = container.Get<IUnityScheduler>();
                    container.TryGet(out IConvaiRoomConnectionService connectionService);
                    container.TryGet(out IConvaiRoomAudioService audioService);
                    return new RuntimeSettingsAudioApplier(settings, microphoneDeviceService, connectionService,
                        audioService, scheduler);
                }));

                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton(container =>
                {
                    var settings = container.Get<IConvaiRuntimeSettingsService>();
                    var transcriptController = container.Get<TranscriptUIController>();
                    return new RuntimeSettingsTranscriptApplier(settings, transcriptController);
                }));

                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton(container =>
                {
                    var settings = container.Get<IConvaiRuntimeSettingsService>();
                    var playerInput = container.Get<IPlayerInputService>();
                    return new RuntimeSettingsPlayerIdentityApplier(settings, playerInput);
                }));

                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered runtime settings services",
                        LogCategory.Bootstrap);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error(
                    $"[ConvaiServiceBootstrap] Failed to register runtime settings services: {ex.Message}",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Registers session-related services (Phase 1 and Phase 2 refactoring).
        ///     These services manage session state, connection lifecycle, and reconnection logic.
        /// </summary>
        private void RegisterSessionServices()
        {
            try
            {
                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<ISessionStateMachine>(container =>
                {
                    var eventHub = container.Get<IEventHub>();
                    container.TryGet(out ILogger logger);
                    return new SessionStateMachine(eventHub, logger);
                }));

                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered ISessionStateMachine",
                        LogCategory.Bootstrap);
                }

                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<ISessionService>(container =>
                {
                    if (!container.TryGet(out ISessionPersistence persistence))
                        persistence = new KeyValueStoreSessionPersistence(new PlayerPrefsKeyValueStore());
                    container.TryGet(out ILogger logger);
                    return new SessionService(persistence, logger);
                }));

                if (_debugLogging)
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered ISessionService", LogCategory.Bootstrap);

                ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<ISessionMetrics>(container =>
                {
                    var stateMachine = container.Get<ISessionStateMachine>();
                    container.TryGet(out ILogger logger);
                    return new SessionMetrics(stateMachine, logger);
                }));

                if (_debugLogging)
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered ISessionMetrics", LogCategory.Bootstrap);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiServiceBootstrap] Failed to register session services: {ex.Message}",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Registers notification-related services.
        ///     - IConvaiNotificationService: Default thread-safe notification service
        ///     - ConvaiNotificationEventBridge: Bridges domain events to notifications
        /// </summary>
        private void RegisterNotificationServices()
        {
            try
            {
                if (_registerDefaultNotificationService)
                {
                    if (!ConvaiServiceLocator.IsRegistered<IConvaiNotificationService>())
                    {
                        ConvaiServiceLocator.Register(
                            ServiceDescriptor.Singleton<IConvaiNotificationService>(container =>
                            {
                                container.TryGet(out IUnityScheduler scheduler);
                                return new ConvaiNotificationService(scheduler);
                            }));

                        if (_debugLogging)
                        {
                            ConvaiLogger.Debug(
                                "[ConvaiServiceBootstrap] Registered IConvaiNotificationService (ConvaiNotificationService)",
                                LogCategory.Bootstrap);
                        }
                    }
                    else if (_debugLogging)
                    {
                        ConvaiLogger.Debug(
                            "[ConvaiServiceBootstrap] IConvaiNotificationService already registered, skipping default",
                            LogCategory.Bootstrap);
                    }
                }
                else if (_debugLogging)
                {
                    ConvaiLogger.Debug(
                        "[ConvaiServiceBootstrap] Skipping default notification service registration (disabled)",
                        LogCategory.Bootstrap);
                }

                // Register ConvaiNotificationEventBridge (always, to bridge domain events)
                if (!ConvaiServiceLocator.IsRegistered<ConvaiNotificationEventBridge>())
                {
                    ConvaiServiceLocator.Register(ServiceDescriptor.Singleton(container =>
                    {
                        var eventHub = container.Get<IEventHub>();
                        var runtimeSettingsService = container.Get<IConvaiRuntimeSettingsService>();

                        Func<IConvaiNotificationService> serviceAccessor = () =>
                        {
                            container.TryGet(out IConvaiNotificationService resolved);
                            return resolved;
                        };

                        Func<bool> isEnabled = () => runtimeSettingsService.Current.NotificationsEnabled;
                        return new ConvaiNotificationEventBridge(serviceAccessor, eventHub, isEnabled);
                    }));

                    if (_debugLogging)
                    {
                        ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered ConvaiNotificationEventBridge",
                            LogCategory.Bootstrap);
                    }
                }

                if (!ConvaiServiceLocator.IsRegistered<RuntimeSettingsNotificationApplier>())
                {
                    if (!ConvaiServiceLocator.IsRegistered<IConvaiNotificationService>())
                    {
                        // Notification service may be registered later by the host project.
                        // The event bridge is late-bound; the applier is optional.
                        return;
                    }

                    ConvaiServiceLocator.Register(ServiceDescriptor.Singleton(container =>
                    {
                        var settingsService = container.Get<IConvaiRuntimeSettingsService>();
                        var notificationService = container.Get<IConvaiNotificationService>();
                        return new RuntimeSettingsNotificationApplier(settingsService, notificationService);
                    }));
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiServiceBootstrap] Failed to register notification services: {ex.Message}",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Registers application-level services.
        ///     Calls ConvaiApplicationServiceRegistrar.RegisterServices() directly since it's in the same assembly.
        /// </summary>
        private void RegisterApplicationServices()
        {
            try
            {
                ConvaiApplicationServiceRegistrar.RegisterServices(_debugLogging);

                if (_debugLogging)
                {
                    ConvaiLogger.Debug(
                        "[ConvaiServiceBootstrap] Application services registered via ConvaiApplicationServiceRegistrar",
                        LogCategory.Bootstrap);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiServiceBootstrap] Failed to register application services: {ex.Message}",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Initializes the ConvaiRoomSession API after all services are registered.
        ///     Direct call to ConvaiRoomSession.Initialize() - no reflection needed since Convai.Runtime references
        ///     Convai.Application.
        /// </summary>
        private void InitializeConvaiRoomSessionApi()
        {
            try
            {
                ConvaiRoomSession.Initialize();
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiServiceBootstrap] Failed to initialize ConvaiRoomSession API: {ex.Message}",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Finds or creates a logger instance.
        ///     ConvaiLogger now directly implements ILogger - no adapter needed.
        /// </summary>
        private ILogger FindOrCreateLogger()
        {
            try
            {
                ConvaiLogger.Initialize();
                return new ConvaiLogger();
            }
            catch (Exception ex)
            {
                if (_debugLogging)
                {
                    ConvaiLogger.Warning($"[ConvaiServiceBootstrap] Failed to create ConvaiLogger: {ex.Message}",
                        LogCategory.Bootstrap);
                }
            }

            return null;
        }

        private void RegisterAudioManager()
        {
            if (!ConvaiServiceLocator.IsRegistered<IConvaiRoomConnectionService>() ||
                !ConvaiServiceLocator.IsRegistered<IConvaiRoomAudioService>())
            {
                if (_debugLogging)
                {
                    ConvaiLogger.Debug(
                        "[ConvaiServiceBootstrap] Skipping DefaultAudioManager registration because room services are unavailable.",
                        LogCategory.Bootstrap);
                }

                return;
            }

            ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<IConvaiAudioManager>(static container =>
            {
                var connectionService = container.Get<IConvaiRoomConnectionService>();
                var audioService = container.Get<IConvaiRoomAudioService>();
                _ = container.TryGet(out ILogger logger);
                return new DefaultAudioManager(connectionService, audioService, logger);
            }));

            if (_debugLogging)
            {
                ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered IConvaiAudioManager (DefaultAudioManager).",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Registers bridge services for room connection and settings.
        ///     Room services (IConvaiRoomConnectionService, IConvaiRoomAudioService) are registered
        ///     by ConvaiCompositionRoot after discovering ConvaiRoomManager in the scene.
        /// </summary>
        private void RegisterRoomServiceAdapters()
        {
            var settingsAdapter = new ConvaiSettingsAdapter();
            ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<IConvaiSettingsProvider>(settingsAdapter));

            if (_debugLogging)
            {
                ConvaiLogger.Debug("[ConvaiServiceBootstrap] Registered IConvaiSettingsProvider bridge",
                    LogCategory.Bootstrap);
            }
        }

        /// <summary>
        ///     Shuts down the Convai SDK and disposes services.
        /// </summary>
        private void Shutdown()
        {
            if (!IsBootstrapped) return;

            if (_debugLogging) ConvaiLogger.Debug("[ConvaiServiceBootstrap] Shutting down...", LogCategory.Bootstrap);

            try
            {
                ConvaiServiceLocator.Shutdown();
                PlatformNetworkingBootstrap.ResetForTests();
                RealtimeTransportFactory.ClearFactory();

                IsBootstrapped = false;

                if (_debugLogging)
                    ConvaiLogger.Debug("[ConvaiServiceBootstrap] Shutdown complete", LogCategory.Bootstrap);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiServiceBootstrap] Shutdown failed: {ex.Message}", LogCategory.Bootstrap);
                ConvaiLogger.Exception(ex, LogCategory.Bootstrap);
            }
        }

        #region Public API for Manual Bootstrap

        /// <summary>
        ///     Manually bootstraps the Convai SDK (useful for testing or custom initialization).
        /// </summary>
        /// <remarks>
        ///     This method can be called from code if you don't want to use the MonoBehaviour.
        ///     It will initialize the service locator and register core services.
        ///     Example:
        ///     <code>
        ///
        /// ConvaiServiceBootstrap.BootstrapManually();
        /// </code>
        /// </remarks>
        internal static void BootstrapManually()
        {
            if (IsBootstrapped)
            {
                if (_instance != null && _instance._debugLogging)
                    ConvaiLogger.Warning("[ConvaiServiceBootstrap] Already bootstrapped", LogCategory.Bootstrap);
                return;
            }

            ConvaiServiceLocator.Initialize();

            var scheduler = UnityScheduler.Instance;
            ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<IUnityScheduler>(scheduler));

            ConvaiServiceLocator.Register(ServiceDescriptor.Singleton<IEventHub>(static container =>
            {
                var schedulerInstance = container.Get<IUnityScheduler>();
                _ = container.TryGet(out ILogger logger);
                return new EventHub(schedulerInstance, logger);
            }));

            PlatformNetworkingBootstrap.EnsureRegistered(_instance != null && _instance._debugLogging);

            ConvaiServiceLocator.InitializeServices();

            IsBootstrapped = true;
        }

        /// <summary>
        ///     Manually shuts down the Convai SDK (useful for testing or cleanup).
        /// </summary>
        public static void ShutdownManually()
        {
            if (!IsBootstrapped) return;

            ConvaiServiceLocator.Shutdown();
            PlatformNetworkingBootstrap.ResetForTests();
            RealtimeTransportFactory.ClearFactory();
            UnityScheduler.Shutdown();
            IsBootstrapped = false;
        }

        #endregion
    }
}
