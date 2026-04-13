using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;

namespace Convai.Infrastructure.Networking.Services
{
    /// <summary>
    ///     Thread-safe implementation of ISessionStateMachine with validated transitions.
    /// </summary>
    public sealed class SessionStateMachine : ISessionStateMachine
    {
        /// <summary>
        ///     Valid state transitions as defined in SessionState documentation.
        /// </summary>
        private static readonly Dictionary<SessionState, HashSet<SessionState>> ValidTransitions = new()
        {
            { SessionState.Disconnected, new HashSet<SessionState> { SessionState.Connecting } },
            { SessionState.Connecting, new HashSet<SessionState> { SessionState.Connected, SessionState.Error } },
            {
                SessionState.Connected,
                new HashSet<SessionState> { SessionState.Disconnecting, SessionState.Reconnecting }
            },
            { SessionState.Reconnecting, new HashSet<SessionState> { SessionState.Connected, SessionState.Error } },
            { SessionState.Disconnecting, new HashSet<SessionState> { SessionState.Disconnected } },
            { SessionState.Error, new HashSet<SessionState> { SessionState.Disconnected } }
        };

        private readonly IEventHub _eventHub;
        private readonly object _lock = new();
        private readonly ILogger _logger;

        private SessionState _currentState = SessionState.Disconnected;
        private string _sessionId;

        /// <summary>
        ///     Creates a new SessionStateMachine.
        /// </summary>
        /// <param name="eventHub">EventHub for publishing SessionStateChanged events (can be null)</param>
        /// <param name="logger">Logger for diagnostic messages (can be null)</param>
        public SessionStateMachine(IEventHub eventHub = null, ILogger logger = null)
        {
            _eventHub = eventHub;
            _logger = logger;
        }

        /// <inheritdoc />
        public SessionState CurrentState
        {
            get
            {
                lock (_lock) return _currentState;
            }
        }

        /// <inheritdoc />
        public string SessionId
        {
            get
            {
                lock (_lock) return _sessionId;
            }
        }

        /// <inheritdoc />
        public event Action<SessionStateChanged> StateChanged;

        /// <inheritdoc />
        public bool TryTransition(SessionState newState, string sessionId = null, string errorCode = null)
        {
            lock (_lock)
            {
                if (!CanTransitionToInternal(newState))
                {
                    _logger?.Warning(
                        $"[SessionStateMachine] Invalid transition: {_currentState} -> {newState}");
                    return false;
                }

                ExecuteTransition(newState, sessionId, errorCode);
                return true;
            }
        }

        /// <inheritdoc />
        public void ForceTransition(SessionState newState, string sessionId = null, string errorCode = null)
        {
            lock (_lock)
            {
                _logger?.Warning(
                    $"[SessionStateMachine] Forcing transition: {_currentState} -> {newState}");
                ExecuteTransition(newState, sessionId, errorCode);
            }
        }

        /// <inheritdoc />
        public void Reset()
        {
            lock (_lock)
            {
                SessionState oldState = _currentState;
                _currentState = SessionState.Disconnected;
                _sessionId = null;

                if (oldState != SessionState.Disconnected)
                    PublishStateChanged(oldState, SessionState.Disconnected, null, null);

                _logger?.Debug("[SessionStateMachine] Reset to Disconnected state");
            }
        }

        /// <inheritdoc />
        public bool CanTransitionTo(SessionState targetState)
        {
            lock (_lock) return CanTransitionToInternal(targetState);
        }

        private bool CanTransitionToInternal(SessionState targetState)
        {
            if (ValidTransitions.TryGetValue(_currentState, out HashSet<SessionState> validTargets))
                return validTargets.Contains(targetState);
            return false;
        }

        private void ExecuteTransition(SessionState newState, string sessionId, string errorCode)
        {
            SessionState oldState = _currentState;
            _currentState = newState;

            if (newState == SessionState.Connected && !string.IsNullOrEmpty(sessionId))
                _sessionId = sessionId;
            else if (newState == SessionState.Disconnected) _sessionId = null;

            _logger?.Debug(
                $"[SessionStateMachine] State transition: {oldState} -> {newState}" +
                (string.IsNullOrEmpty(errorCode) ? "" : $" (error: {errorCode})"));

            PublishStateChanged(oldState, newState, _sessionId, errorCode);
        }

        private void PublishStateChanged(SessionState oldState, SessionState newState, string sessionId,
            string errorCode)
        {
            var evt = SessionStateChanged.Create(oldState, newState, sessionId, errorCode);

            StateChanged?.Invoke(evt);

            _eventHub?.Publish(evt);
        }
    }
}
