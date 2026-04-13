namespace Convai.Domain.DomainEvents.Session
{
    /// <summary>
    ///     Represents the current state of a Convai session.
    ///     Standardizes session lifecycle tracking across the SDK.
    /// </summary>
    /// <remarks>
    ///     This enum replaces ad-hoc boolean flags and callbacks in the room controller.
    ///     Services can subscribe to SessionStateChanged events via EventHub instead of
    ///     registering multiple callbacks.
    ///     State Transitions:
    ///     - Disconnected → Connecting (user initiates connection)
    ///     - Connecting → Connected (connection established)
    ///     - Connecting → Error (connection failed)
    ///     - Connected → Disconnecting (user initiates disconnect)
    ///     - Connected → Reconnecting (connection lost, attempting recovery)
    ///     - Reconnecting → Connected (reconnection successful)
    ///     - Reconnecting → Error (reconnection failed)
    ///     - Disconnecting → Disconnected (clean disconnect)
    ///     - Error → Disconnected (error acknowledged)
    ///     Usage:
    ///     <code>
    /// 
    /// private readonly IEventHub _eventHub;
    /// 
    /// _eventHub.Subscribe&lt;SessionStateChanged&gt;(this, e =>
    /// {
    ///     switch (e.NewState)
    ///     {
    ///         case SessionState.Connected:
    ///             Debug.Log("Session connected!");
    ///             break;
    ///         case SessionState.Error:
    ///             Debug.LogError($"Session error: {e.ErrorCode}");
    ///             break;
    ///     }
    /// });
    /// </code>
    /// </remarks>
    public enum SessionState
    {
        /// <summary>
        ///     No active session. Initial state and final state after clean disconnect.
        /// </summary>
        Disconnected = 0,

        /// <summary>
        ///     Attempting to establish a new session connection.
        ///     Transitioning from Disconnected to Connected.
        /// </summary>
        Connecting = 1,

        /// <summary>
        ///     Session is active and operational. Can send/receive data.
        /// </summary>
        Connected = 2,

        /// <summary>
        ///     Connection was lost, attempting to reconnect automatically.
        ///     Transitioning from Connected back to Connected (or Error).
        /// </summary>
        Reconnecting = 3,

        /// <summary>
        ///     Gracefully closing the session.
        ///     Transitioning from Connected to Disconnected.
        /// </summary>
        Disconnecting = 4,

        /// <summary>
        ///     Session encountered an unrecoverable error.
        ///     Requires manual intervention to recover.
        /// </summary>
        Error = 5
    }

    /// <summary>
    ///     Extension methods for SessionState enum.
    /// </summary>
    public static class SessionStateExtensions
    {
        /// <summary>
        ///     Checks if the session is in a connected state (Connected or Reconnecting).
        /// </summary>
        /// <param name="state">The session state to check</param>
        /// <returns>True if connected or reconnecting, false otherwise</returns>
        public static bool IsConnected(this SessionState state) =>
            state == SessionState.Connected || state == SessionState.Reconnecting;

        /// <summary>
        ///     Checks if the session is in a transitional state (Connecting, Reconnecting, Disconnecting).
        /// </summary>
        /// <param name="state">The session state to check</param>
        /// <returns>True if in transition, false otherwise</returns>
        public static bool IsTransitioning(this SessionState state)
        {
            return state == SessionState.Connecting
                   || state == SessionState.Reconnecting
                   || state == SessionState.Disconnecting;
        }

        /// <summary>
        ///     Checks if the session is in a stable state (Disconnected, Connected, Error).
        /// </summary>
        /// <param name="state">The session state to check</param>
        /// <returns>True if stable, false otherwise</returns>
        public static bool IsStable(this SessionState state)
        {
            return state == SessionState.Disconnected
                   || state == SessionState.Connected
                   || state == SessionState.Error;
        }

        /// <summary>
        ///     Checks if the session can accept user input/commands.
        /// </summary>
        /// <param name="state">The session state to check</param>
        /// <returns>True if can accept input, false otherwise</returns>
        public static bool CanAcceptInput(this SessionState state) => state == SessionState.Connected;
    }
}
