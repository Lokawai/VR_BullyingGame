using System;

namespace Convai.Domain.DomainEvents.Session
{
    /// <summary>
    ///     Domain event raised when a session transitions from one state to another.
    ///     Replaces ad-hoc callbacks with structured event-based communication via EventHub.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub whenever the session state changes.
    ///     Services can subscribe to this event instead of registering multiple callbacks
    ///     on ConvaiRoomManager (OnConnected, OnDisconnected, etc.).
    ///     Integration Example:
    ///     <code>
    /// 
    /// private readonly IEventHub _eventHub;
    /// 
    /// private void OnLiveKitConnected()
    /// {
    ///     SessionState oldState = _currentState;
    ///     _currentState = SessionState.Connected;
    /// 
    ///     _eventHub.Publish(new SessionStateChanged(
    ///         oldState: oldState,
    ///         newState: SessionState.Connected,
    ///         sessionId: _sessionId,
    ///         timestamp: DateTime.UtcNow
    ///     ));
    /// }
    /// 
    /// 
    /// _eventHub.Subscribe&lt;SessionStateChanged&gt;(this, e =>
    /// {
    ///     if (e.NewState == SessionState.Connected)
    ///     {
    ///         Debug.Log($"Session {e.SessionId} connected at {e.Timestamp}");
    ///     }
    /// });
    /// </code>
    /// </remarks>
    public readonly struct SessionStateChanged
    {
        /// <summary>
        ///     The previous session state.
        /// </summary>
        public SessionState OldState { get; }

        /// <summary>
        ///     The new session state.
        /// </summary>
        public SessionState NewState { get; }

        /// <summary>
        ///     The session identifier (can be null if not yet assigned).
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        ///     When the state change occurred (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Optional error code if transitioning to Error state.
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        ///     Creates a new SessionStateChanged event.
        /// </summary>
        public SessionStateChanged(
            SessionState oldState,
            SessionState newState,
            string sessionId,
            DateTime timestamp,
            string errorCode = null)
        {
            OldState = oldState;
            NewState = newState;
            SessionId = sessionId;
            Timestamp = timestamp;
            ErrorCode = errorCode;
        }

        /// <summary>
        ///     Creates a SessionStateChanged event with the current UTC timestamp.
        /// </summary>
        /// <param name="oldState">The previous session state</param>
        /// <param name="newState">The new session state</param>
        /// <param name="sessionId">The session identifier</param>
        /// <param name="errorCode">Optional error code if transitioning to Error state</param>
        /// <returns>A new SessionStateChanged event</returns>
        public static SessionStateChanged Create(
            SessionState oldState,
            SessionState newState,
            string sessionId,
            string errorCode = null)
        {
            return new SessionStateChanged(
                oldState,
                newState,
                sessionId,
                DateTime.UtcNow,
                errorCode
            );
        }

        /// <summary>
        ///     Checks if this state change represents a successful connection.
        /// </summary>
        public bool IsConnectionEstablished =>
            OldState == SessionState.Connecting && NewState == SessionState.Connected;

        /// <summary>
        ///     Checks if this state change represents a successful reconnection.
        /// </summary>
        public bool IsReconnectionSuccessful =>
            OldState == SessionState.Reconnecting && NewState == SessionState.Connected;

        /// <summary>
        ///     Checks if this state change represents a disconnection.
        /// </summary>
        public bool IsDisconnected =>
            NewState == SessionState.Disconnected;

        /// <summary>
        ///     Checks if this state change represents an error.
        /// </summary>
        public bool IsError =>
            NewState == SessionState.Error;

        /// <summary>
        ///     Checks if this state change represents entering a reconnecting state.
        /// </summary>
        public bool IsReconnecting =>
            OldState == SessionState.Connected && NewState == SessionState.Reconnecting;
    }
}
