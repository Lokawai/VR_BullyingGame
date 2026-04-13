using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Convai.Application;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Facades;
using Convai.Runtime.Logging;
using Convai.Shared.DependencyInjection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Canonical scene/runtime entrypoint for Convai SDK integration.
    ///     Ensures all required core components exist and exposes high-level room/audio operations.
    /// </summary>
    [AddComponentMenu("Convai/Convai Manager")]
    [DefaultExecutionOrder(-1100)]
    [DisallowMultipleComponent]
    public class ConvaiManager : MonoBehaviour
    {
        [Header("Manager Settings")]
        [SerializeField]
        [Tooltip("If enabled, ConvaiManager persists across scene loads.")]
        private bool _persistAcrossScenes = true;

        [SerializeField]
        [Tooltip(
            "If enabled, ConvaiManager keeps the Characters and Player references updated by scanning loaded scenes.")]
        private bool _autoDiscoverSceneAgents = true;

        [SerializeField] [Tooltip("If enabled, discovery includes inactive GameObjects.")]
        private bool _includeInactiveInDiscovery = true;

        [SerializeField] [Tooltip("If enabled, logs manager lifecycle and setup steps.")]
        private bool _debugLogging;

        [SerializeField]
        [Tooltip(
            "On WebGL, arms the next non-UI scene click after room connection to enable audio playback and start microphone capture.")]
        private bool _enableVoiceOnFirstSceneClickAfterConnectInWebGL = true;

        [Header("Managed Components")] [SerializeField] [HideInInspector]
        private ConvaiServiceBootstrap _serviceBootstrap;

        [SerializeField] [HideInInspector] private ConvaiCompositionRoot _compositionRoot;
        [SerializeField] [HideInInspector] private ConvaiRoomManager _roomManager;

        private readonly List<ConvaiCharacter> _characters = new();
        private ConvaiAudio _audio;
        private ConvaiEvents _events;
        private bool _sdkEventsSubscribed;
        private bool _webGLVoiceStartArmed;

        /// <summary>Singleton instance for easy global access.</summary>
        public static ConvaiManager Instance { get; private set; }

        /// <summary>True when the bootstrap + ConvaiRoomSession API are initialized.</summary>
        public bool IsInitialized => ConvaiServiceBootstrap.IsBootstrapped && ConvaiRoomSession.IsInitialized;

        /// <summary>True when currently connected to a Convai room.</summary>
        public bool IsConnected =>
            _roomManager != null ? _roomManager.IsConnected : ConvaiRoomSession.IsConnectedToRoom;

        /// <summary>Known Convai characters in loaded scenes.</summary>
        public IReadOnlyList<ConvaiCharacter> Characters => _characters;

        /// <summary>Known Convai player in loaded scenes (first found).</summary>
        public ConvaiPlayer Player { get; private set; }

        /// <summary>High-level event facade for common SDK events.</summary>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if accessed before ConvaiManager has completed initialization.
        ///     Ensure ConvaiManager is in the scene and has run its Start() before accessing Events.
        /// </exception>
        public ConvaiEvents Events
        {
            get
            {
                EnsureFacades();
                if (_events == null)
                {
                    throw new InvalidOperationException(
                        "[ConvaiManager] Events is not available yet. " +
                        "Ensure ConvaiManager has completed initialization before accessing Events. " +
                        "This typically means waiting until Start() has run and ConvaiServiceBootstrap is complete.");
                }

                return _events;
            }
        }

        /// <summary>High-level audio facade for microphone and character audio controls.</summary>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if accessed before ConvaiManager has completed initialization.
        ///     Ensure ConvaiManager is in the scene and a ConvaiRoomManager component exists on the same GameObject.
        /// </exception>
        public ConvaiAudio Audio
        {
            get
            {
                EnsureFacades();
                if (_audio == null)
                {
                    throw new InvalidOperationException(
                        "[ConvaiManager] Audio is not available yet. " +
                        "Ensure ConvaiManager has completed initialization and a ConvaiRoomManager component exists. " +
                        "This typically means waiting until Start() has run.");
                }

                return _audio;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (_debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiManager] Duplicate manager detected; destroying duplicate instance.",
                        LogCategory.SDK);
                }

                DestroyImmediate(gameObject);
                return;
            }

            Instance = this;

            if (_persistAcrossScenes)
            {
                if (transform.parent != null) transform.SetParent(null);

                if (UnityEngine.Application.isPlaying) DontDestroyOnLoad(gameObject);
            }

            EnsureRequiredCoreComponents();
        }

        private void Start()
        {
            EnsureRequiredCoreComponents();
            RefreshSceneReferences();
            EnsureFacades();
            UpdateWebGLVoiceStartArmState();
        }

        private void Update() => TryConsumeWebGLVoiceStartGesture();

        private void OnEnable()
        {
            SubscribeSdkEvents();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            UnsubscribeSdkEvents();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _webGLVoiceStartArmed = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            if (_events != null)
            {
                _events.Dispose();
                _events = null;
            }
        }

        /// <summary>Raised when room connection succeeds.</summary>
        public event Action OnConnected;

        /// <summary>Raised when room disconnects.</summary>
        public event Action OnDisconnected;

        /// <summary>Raised when manager detects an operational error. Parameter: structured SessionError.</summary>
        public event Action<SessionError> OnError;

        /// <summary>
        ///     Initiates room connection using the managed room manager.
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (!TryGetRoomManager(out ConvaiRoomManager roomManager)) return false;

            try
            {
                return await roomManager.ConnectAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(SessionError.Create(
                    SessionErrorCodes.ConnectionFailed,
                    $"Convai connection failed: {ex.Message}",
                    exception: ex));
                return false;
            }
        }

        /// <summary>
        ///     Disconnects the active room connection using the managed room manager.
        /// </summary>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!TryGetRoomManager(out ConvaiRoomManager roomManager)) return;

            try
            {
                await roomManager.DisconnectAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(SessionError.Create(
                    SessionErrorCodes.ConnectionFailed,
                    $"Convai disconnect failed: {ex.Message}",
                    exception: ex));
            }
        }

        /// <summary>
        ///     Starts microphone capture using the managed room manager.
        ///     Useful for wiring UI buttons directly to the manager.
        /// </summary>
        public void StartListening()
        {
            if (!TryGetRoomManager(out ConvaiRoomManager roomManager)) return;

            roomManager.StartListening();
        }

        /// <summary>
        ///     Toggles local microphone mute state using the managed room manager.
        ///     Useful for wiring UI buttons directly to the manager.
        /// </summary>
        public bool ToggleMicMute()
        {
            if (!TryGetRoomManager(out ConvaiRoomManager roomManager)) return false;

            return roomManager.ToggleMicMute();
        }

        /// <summary>
        ///     Enables audio playback and starts microphone capture in a single user gesture flow.
        ///     Intended for browser platforms that require explicit user interaction before audio starts.
        /// </summary>
        public void EnableAudioAndStartListening()
        {
            if (!TryGetRoomManager(out ConvaiRoomManager roomManager)) return;

            roomManager.EnableAudioAndStartListening();
        }

        /// <summary>
        ///     Re-runs scene discovery for ConvaiCharacter and ConvaiPlayer components.
        /// </summary>
        public void RefreshReferences() => RefreshSceneReferences();

        private void EnsureRequiredCoreComponents()
        {
            _serviceBootstrap = EnsureComponent(_serviceBootstrap);
            _roomManager = EnsureComponent(_roomManager);
            _compositionRoot = EnsureComponent(_compositionRoot);
        }

        private void EnsureRoomManagerReference()
        {
            if (_roomManager != null) return;

            _roomManager = FindExistingComponent<ConvaiRoomManager>();
            if (_roomManager == null)
            {
                _roomManager = gameObject.GetComponent<ConvaiRoomManager>() ??
                               gameObject.AddComponent<ConvaiRoomManager>();
            }
        }

        private bool TryGetRoomManager(out ConvaiRoomManager roomManager)
        {
            EnsureRoomManagerReference();
            roomManager = _roomManager;
            if (roomManager != null) return true;

            const string message =
                "ConvaiRoomManager is not available. Add ConvaiManager to the scene or run setup again.";
            OnError?.Invoke(SessionError.Create(
                SessionErrorCodes.SessionInvalidState,
                message));
            return false;
        }

        private void EnsureFacades()
        {
            if (_audio == null)
            {
                EnsureRoomManagerReference();
                if (_roomManager != null) _audio = new ConvaiAudio(_roomManager);
            }

            if (_events == null && ConvaiServiceLocator.TryGet(out IEventHub eventHub))
                _events = new ConvaiEvents(eventHub);
        }

        private void RefreshSceneReferences()
        {
            if (!_autoDiscoverSceneAgents) return;

            FindObjectsInactive includeInactive = _includeInactiveInDiscovery
                ? FindObjectsInactive.Include
                : FindObjectsInactive.Exclude;

            _characters.Clear();
            ConvaiCharacter[] discoveredCharacters =
                FindObjectsByType<ConvaiCharacter>(includeInactive, FindObjectsSortMode.None);
            if (discoveredCharacters != null && discoveredCharacters.Length > 0)
                _characters.AddRange(discoveredCharacters);

            ConvaiPlayer[] discoveredPlayers =
                FindObjectsByType<ConvaiPlayer>(includeInactive, FindObjectsSortMode.None);
            Player = discoveredPlayers != null && discoveredPlayers.Length > 0 ? discoveredPlayers[0] : null;
        }

        private void SubscribeSdkEvents()
        {
            if (_sdkEventsSubscribed) return;

            ConvaiRoomSession.OnRoomConnected += HandleRoomConnected;
            ConvaiRoomSession.OnRoomDisconnected += HandleRoomDisconnected;
            ConvaiRoomSession.OnRoomConnectionFailed += HandleRoomConnectionFailed;
            _sdkEventsSubscribed = true;
        }

        private void UnsubscribeSdkEvents()
        {
            if (!_sdkEventsSubscribed) return;

            ConvaiRoomSession.OnRoomConnected -= HandleRoomConnected;
            ConvaiRoomSession.OnRoomDisconnected -= HandleRoomDisconnected;
            ConvaiRoomSession.OnRoomConnectionFailed -= HandleRoomConnectionFailed;
            _sdkEventsSubscribed = false;
        }

        private void HandleRoomConnected()
        {
            RefreshSceneReferences();
            UpdateWebGLVoiceStartArmState();
            OnConnected?.Invoke();
        }

        private void HandleRoomDisconnected()
        {
            _webGLVoiceStartArmed = false;
            OnDisconnected?.Invoke();
        }

        private void HandleRoomConnectionFailed()
        {
            OnError?.Invoke(SessionError.Create(
                SessionErrorCodes.ConnectionFailed,
                "Convai room connection failed."));
        }

        private void OnSceneLoaded(Scene _, LoadSceneMode __)
        {
            EnsureRequiredCoreComponents();
            RefreshSceneReferences();
            EnsureFacades();
            UpdateWebGLVoiceStartArmState();
        }

        private void UpdateWebGLVoiceStartArmState()
        {
            if (!ShouldArmWebGLVoiceStart())
            {
                _webGLVoiceStartArmed = false;
                return;
            }

            _webGLVoiceStartArmed = true;
        }

        private bool ShouldArmWebGLVoiceStart()
        {
            if (!CanArmWebGLVoiceStart(
                    _enableVoiceOnFirstSceneClickAfterConnectInWebGL,
                    UnityEngine.Application.platform,
                    TryGetRoomManager(out ConvaiRoomManager roomManager),
                    roomManager?.IsConnected ?? false,
                    roomManager?.RequiresUserGestureForAudio ?? false,
                    roomManager?.IsAudioPlaybackActive ?? false))
                return false;

            return true;
        }

        private void TryConsumeWebGLVoiceStartGesture()
        {
            if (!_webGLVoiceStartArmed) return;

            if (!ShouldArmWebGLVoiceStart())
            {
                _webGLVoiceStartArmed = false;
                return;
            }

            if (!ShouldConsumeWebGLVoiceStartGesture(WasScenePointerPressedThisFrame(), IsPointerOverUI())) return;

            _webGLVoiceStartArmed = false;

            if (_debugLogging)
            {
                ConvaiLogger.Debug(
                    "[ConvaiManager] Consuming first WebGL scene interaction to enable audio playback and microphone capture.",
                    LogCategory.SDK);
            }

            EnableAudioAndStartListening();
        }

        private static bool CanArmWebGLVoiceStart(
            bool featureEnabled,
            RuntimePlatform platform,
            bool hasRoomManager,
            bool isConnected,
            bool requiresUserGestureForAudio,
            bool isAudioPlaybackActive)
        {
            return featureEnabled
                   && platform == RuntimePlatform.WebGLPlayer
                   && hasRoomManager
                   && isConnected
                   && requiresUserGestureForAudio
                   && !isAudioPlaybackActive;
        }

        private static bool ShouldConsumeWebGLVoiceStartGesture(bool pointerPressedThisFrame, bool pointerOverUi) =>
            pointerPressedThisFrame && !pointerOverUi;

        private static bool WasScenePointerPressedThisFrame()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0)) return true;

            for (int i = 0; i < Input.touchCount; i++)
            {
                if (Input.GetTouch(i).phase == TouchPhase.Began)
                    return true;
            }
#endif

#if ENABLE_INPUT_SYSTEM
            if (WasPressedThisFrame("UnityEngine.InputSystem.Mouse, Unity.InputSystem", "leftButton")) return true;

            return WasPressedThisFrame("UnityEngine.InputSystem.Touchscreen, Unity.InputSystem", "primaryTouch",
                "press");
#else
            return false;
#endif
        }

        private static bool WasPressedThisFrame(string typeName, params string[] memberPath)
        {
            var type = Type.GetType(typeName);
            if (type == null) return false;

            object device = type.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (device == null) return false;

            object current = device;
            for (int i = 0; i < memberPath.Length; i++)
            {
                current = current?.GetType().GetProperty(memberPath[i], BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(current);
                if (current == null) return false;
            }

            return current.GetType().GetProperty("wasPressedThisFrame", BindingFlags.Public | BindingFlags.Instance)
                       ?.GetValue(current) is bool wasPressed
                   && wasPressed;
        }

        private static bool IsPointerOverUI()
        {
            EventSystem current = EventSystem.current;
            if (current == null) return false;

#if ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began && current.IsPointerOverGameObject(touch.fingerId)) return true;
            }
#endif

            return current.IsPointerOverGameObject();
        }

        private T EnsureComponent<T>(T current) where T : Component
        {
            if (current != null) return current;

            var existing = FindExistingComponent<T>();
            if (existing != null) return existing;

            return gameObject.GetComponent<T>() ?? gameObject.AddComponent<T>();
        }

        private static T FindExistingComponent<T>() where T : Component
        {
            T[] found = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return found != null && found.Length > 0 ? found[0] : null;
        }
    }
}
