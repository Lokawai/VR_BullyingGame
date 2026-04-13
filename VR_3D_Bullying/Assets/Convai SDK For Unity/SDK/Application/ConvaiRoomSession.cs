using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Participant;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Shared.DependencyInjection;

namespace Convai.Application
{
    /// <summary>
    ///     Canonical room/session API for runtime connection state and room-level callbacks.
    /// </summary>
    /// <remarks>
    ///     ConvaiRoomSession is a static room-level API for runtime session state and callbacks.
    ///     For scene setup and gameplay integration, use ConvaiManager as the primary entry point.
    ///     It provides:
    ///     - Thread-safe initialization status
    ///     - Room connection events (Connected, Disconnected, ConnectionFailed)
    ///     - Participant events (Joined, Left)
    ///     All public events are guaranteed to be raised on the Unity main thread for safe UI access.
    ///     Note: For Character-specific conversation control, use ConvaiCharacter.StartConversationAsync() /
    ///     StopConversationAsync().
    ///     ConvaiRoomSession exposes room-level events that affect all Characters in a session.
    ///     Usage:
    ///     <code>
    /// 
    /// if (ConvaiRoomSession.IsInitialized)
    /// {
    ///     Debug.Log($"Convai SDK v{ConvaiSDK.Version}");
    ///     ConvaiRoomSession.OnRoomConnected += () => Debug.Log("Room connected!");
    ///     ConvaiRoomSession.OnParticipantJoined += (info) => Debug.Log($"Participant joined: {info.DisplayName}");
    /// }
    /// 
    /// 
    /// ConvaiRoomSession.Shutdown();
    /// </code>
    /// </remarks>
    public static class ConvaiRoomSession
    {
        private static int _initialized;
        private static IConvaiSettingsProvider _settingsProvider;
        private static IEventHub _eventHub;

        private static SubscriptionToken? _sessionStateToken;
        private static SubscriptionToken? _participantConnectedToken;
        private static SubscriptionToken? _participantDisconnectedToken;

        private static int _isConnected;
        private static readonly object _eventLock = new();

        private static Action _onRoomConnected;
        private static Action _onRoomDisconnected;
        private static Action _onRoomConnectionFailed;
        private static Action<ParticipantInfo> _onParticipantJoined;
        private static Action<ParticipantInfo> _onParticipantLeft;

        /// <summary>
        ///     Gets a value indicating whether the SDK has been initialized.
        /// </summary>
        public static bool IsInitialized => Interlocked.CompareExchange(ref _initialized, 0, 0) == 1;

        /// <summary>
        ///     Gets a value indicating whether the SDK is currently connected to a room.
        /// </summary>
        public static bool IsConnectedToRoom => Interlocked.CompareExchange(ref _isConnected, 0, 0) == 1;

        /// <summary>
        ///     Raised when the SDK successfully connects to a Convai room.
        /// </summary>
        public static event Action OnRoomConnected
        {
            add
            {
                lock (_eventLock) _onRoomConnected += value;
            }
            remove
            {
                lock (_eventLock) _onRoomConnected -= value;
            }
        }

        /// <summary>
        ///     Raised when the SDK disconnects from a Convai room.
        /// </summary>
        public static event Action OnRoomDisconnected
        {
            add
            {
                lock (_eventLock) _onRoomDisconnected += value;
            }
            remove
            {
                lock (_eventLock) _onRoomDisconnected -= value;
            }
        }

        /// <summary>
        ///     Raised when a room connection attempt fails.
        /// </summary>
        public static event Action OnRoomConnectionFailed
        {
            add
            {
                lock (_eventLock) _onRoomConnectionFailed += value;
            }
            remove
            {
                lock (_eventLock) _onRoomConnectionFailed -= value;
            }
        }

        /// <summary>
        ///     Raised when a participant joins the room.
        /// </summary>
        public static event Action<ParticipantInfo> OnParticipantJoined
        {
            add
            {
                lock (_eventLock) _onParticipantJoined += value;
            }
            remove
            {
                lock (_eventLock) _onParticipantJoined -= value;
            }
        }

        /// <summary>
        ///     Raised when a participant leaves the room.
        /// </summary>
        public static event Action<ParticipantInfo> OnParticipantLeft
        {
            add
            {
                lock (_eventLock) _onParticipantLeft += value;
            }
            remove
            {
                lock (_eventLock) _onParticipantLeft -= value;
            }
        }

        /// <summary>
        ///     Initializes the ConvaiRoomSession API with default settings.
        ///     This is called automatically during manager-driven bootstrap after core services are registered.
        /// </summary>
        /// <remarks>
        ///     This method is thread-safe and idempotent. Multiple calls will be ignored after first successful init.
        ///     Manual initialization is only needed if not using ConvaiManager bootstrap.
        ///     Resolves required services from the service locator.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Initialize()
        {
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 1) return;

            ResolveServices();
            SubscribeToEvents();
        }

        /// <summary>
        ///     Shuts down the ConvaiRoomSession, releasing all resources and clearing all event subscriptions.
        /// </summary>
        /// <remarks>
        ///     This method is safe to call multiple times (idempotent).
        ///     After calling Shutdown(), you can call Initialize() again to reinitialize the SDK.
        ///     All event handlers will be cleared, so you must resubscribe after re-initialization.
        /// </remarks>
        public static void Shutdown()
        {
            if (Interlocked.CompareExchange(ref _initialized, 0, 0) == 0) return;

            UnsubscribeFromEvents();
            _settingsProvider = null;
            _eventHub = null;
            _sessionStateToken = null;
            _participantConnectedToken = null;
            _participantDisconnectedToken = null;
            Interlocked.Exchange(ref _isConnected, 0);
            Interlocked.Exchange(ref _initialized, 0);

            lock (_eventLock)
            {
                _onRoomConnected = null;
                _onRoomDisconnected = null;
                _onRoomConnectionFailed = null;
                _onParticipantJoined = null;
                _onParticipantLeft = null;
            }
        }

        /// <summary>
        ///     Resets the SDK to uninitialized state.
        ///     This is primarily used for testing and cleanup scenarios.
        /// </summary>
        /// <remarks>
        ///     This method is equivalent to <see cref="Shutdown" /> but is internal.
        ///     Prefer using <see cref="Shutdown" /> for public API access.
        /// </remarks>
        internal static void Reset() => Shutdown();

        /// <summary>
        ///     Resolves services from the service locator using typed interfaces.
        ///     No reflection needed - uses Domain layer abstractions.
        /// </summary>
        private static void ResolveServices()
        {
            if (!ConvaiServiceLocator.IsInitialized) return;

            ConvaiServiceLocator.TryGet(out _settingsProvider);
            ConvaiServiceLocator.TryGet(out _eventHub);
        }

        private static void SubscribeToEvents()
        {
            if (_eventHub == null) return;

            _sessionStateToken = _eventHub.Subscribe<SessionStateChanged>(HandleSessionStateChanged);
            _participantConnectedToken = _eventHub.Subscribe<ParticipantConnected>(HandleParticipantConnected);
            _participantDisconnectedToken = _eventHub.Subscribe<ParticipantDisconnected>(HandleParticipantDisconnected);
        }

        private static void UnsubscribeFromEvents()
        {
            if (_eventHub == null) return;

            if (_sessionStateToken.HasValue)
            {
                _eventHub.Unsubscribe(_sessionStateToken.Value);
                _sessionStateToken = null;
            }

            if (_participantConnectedToken.HasValue)
            {
                _eventHub.Unsubscribe(_participantConnectedToken.Value);
                _participantConnectedToken = null;
            }

            if (_participantDisconnectedToken.HasValue)
            {
                _eventHub.Unsubscribe(_participantDisconnectedToken.Value);
                _participantDisconnectedToken = null;
            }
        }

        private static void HandleSessionStateChanged(SessionStateChanged e)
        {
            if (e.NewState == SessionState.Connected || e.NewState == SessionState.Reconnecting)
                Interlocked.Exchange(ref _isConnected, 1);
            else
                Interlocked.Exchange(ref _isConnected, 0);

            switch (e.NewState)
            {
                case SessionState.Connected:
                    {
                        // Initial connect or reconnection.
                        if (e.OldState == SessionState.Connecting || e.OldState == SessionState.Reconnecting)
                        {
                            Action handler;
                            lock (_eventLock) handler = _onRoomConnected;
                            handler?.Invoke();
                        }

                        break;
                    }
                case SessionState.Disconnected:
                    {
                        if (e.OldState != SessionState.Disconnected)
                        {
                            Action handler;
                            lock (_eventLock) handler = _onRoomDisconnected;
                            handler?.Invoke();
                        }

                        break;
                    }
                case SessionState.Error:
                    {
                        // Treat any transition into Error as a connection failure.
                        Action handler;
                        lock (_eventLock) handler = _onRoomConnectionFailed;
                        handler?.Invoke();
                        break;
                    }
            }
        }

        private static void HandleParticipantConnected(ParticipantConnected e)
        {
            Action<ParticipantInfo> handler;
            lock (_eventLock) handler = _onParticipantJoined;
            handler?.Invoke(e.Participant);
        }

        private static void HandleParticipantDisconnected(ParticipantDisconnected e)
        {
            Action<ParticipantInfo> handler;
            lock (_eventLock) handler = _onParticipantLeft;
            handler?.Invoke(e.Participant);
        }
    }
}
