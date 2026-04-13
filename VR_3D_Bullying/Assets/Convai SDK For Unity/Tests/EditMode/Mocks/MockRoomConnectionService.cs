using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Room;

namespace Convai.Tests.EditMode.Mocks
{
    /// <summary>
    ///     Lightweight mock for IConvaiRoomConnectionService used in edit-mode tests.
    /// </summary>
    public sealed class MockRoomConnectionService : IConvaiRoomConnectionService
    {
        // --- Messaging stubs (record calls for assertions) ---

        public List<(string Name, string Message)> SentTriggers { get; } = new();
        public List<string> SentDynamicInfos { get; } = new();
        public List<Dictionary<string, string>> SentTemplateKeys { get; } = new();
        public event Action Connected;
        public event Action ConnectionFailed;
        public event Action<SessionStateChanged> OnSessionStateChanged;

        public SessionState CurrentState { get; private set; } = SessionState.Disconnected;
        public bool IsConnected { get; private set; }
        public bool HasRoomDetails { get; set; }
        public IRoomFacade CurrentRoom { get; set; }
        public RTVIHandler RtvHandler { get; set; }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            SessionState oldState = CurrentState;
            CurrentState = SessionState.Connected;
            IsConnected = true;
            Connected?.Invoke();
            OnSessionStateChanged?.Invoke(SessionStateChanged.Create(oldState, CurrentState, "mock-session"));
            return Task.FromResult(true);
        }

        public Task DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
            CancellationToken cancellationToken = default)
        {
            SessionState oldState = CurrentState;
            CurrentState = SessionState.Disconnected;
            IsConnected = false;
            OnSessionStateChanged?.Invoke(SessionStateChanged.Create(oldState, CurrentState, "mock-session"));
            return Task.CompletedTask;
        }

        public bool SendTrigger(string triggerName, string triggerMessage = null)
        {
            SentTriggers.Add((triggerName, triggerMessage));
            return true;
        }

        public bool SendDynamicInfo(string contextText)
        {
            SentDynamicInfos.Add(contextText);
            return true;
        }

        public bool UpdateTemplateKeys(Dictionary<string, string> templateKeys)
        {
            SentTemplateKeys.Add(templateKeys);
            return true;
        }

        public void RaiseConnected()
        {
            SessionState oldState = CurrentState;
            CurrentState = SessionState.Connected;
            IsConnected = true;
            Connected?.Invoke();
            OnSessionStateChanged?.Invoke(SessionStateChanged.Create(oldState, CurrentState, "mock-session"));
        }

        public void RaiseConnectionFailed()
        {
            SessionState oldState = CurrentState;
            CurrentState = SessionState.Error;
            ConnectionFailed?.Invoke();
            OnSessionStateChanged?.Invoke(SessionStateChanged.Create(oldState, CurrentState, "mock-session"));
        }
    }
}
