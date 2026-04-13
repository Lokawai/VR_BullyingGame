using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.Session;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Transport;

namespace Convai.Runtime.Room
{
    /// <summary>
    ///     Provides Unity-facing access to Convai room connection state, events, and room surfaces.
    ///     Uses platform-agnostic abstractions for cross-platform compatibility.
    /// </summary>
    public interface IConvaiRoomConnectionService
    {
        /// <summary>
        ///     Gets the current session state.
        /// </summary>
        public SessionState CurrentState { get; }

        /// <summary>
        ///     Indicates whether the SDK is currently connected to the Convai room.
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        ///     Indicates whether the room has valid details (token, session, LiveKit room).
        /// </summary>
        public bool HasRoomDetails { get; }

        /// <summary>
        ///     Gets the active room facade (null until connected).
        ///     Provides platform-agnostic access to room state and participants.
        /// </summary>
        public IRoomFacade CurrentRoom { get; }

        /// <summary>
        ///     Gets the RTVI handler for publishing transport payloads.
        /// </summary>
        public RTVIHandler RtvHandler { get; }

        /// <summary>
        ///     Raised whenever the room connection succeeds.
        /// </summary>
        public event Action Connected;

        /// <summary>
        ///     Raised whenever the room connection fails.
        /// </summary>
        public event Action ConnectionFailed;

        /// <summary>
        ///     Raised whenever the session state changes.
        ///     Unity-safe convenience event; EventHub also publishes SessionStateChanged for decoupled consumers.
        /// </summary>
        public event Action<SessionStateChanged> OnSessionStateChanged;

        /// <summary>
        ///     Initiates a connection workflow using the configured room manager.
        /// </summary>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        /// <returns>True when the connection succeeds; otherwise false.</returns>
        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Disconnects from the Convai room for the supplied reason.
        /// </summary>
        /// <param name="reason">High-level disconnect reason.</param>
        /// <param name="cancellationToken">Token used to cancel the operation.</param>
        public Task DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Sends a trigger event to the conversation backend.
        /// </summary>
        /// <param name="triggerName">Name of the trigger to send.</param>
        /// <param name="triggerMessage">Optional message payload.</param>
        /// <returns>True if the message was sent; false if the connection is not ready.</returns>
        public bool SendTrigger(string triggerName, string triggerMessage = null);

        /// <summary>
        ///     Sends dynamic context information to the backend.
        ///     This is injected as a context update for the character.
        /// </summary>
        /// <param name="contextText">The dynamic context text to send.</param>
        /// <returns>True if the message was sent; false if the connection is not ready.</returns>
        public bool SendDynamicInfo(string contextText);

        /// <summary>
        ///     Updates template keys for narrative design placeholder resolution.
        ///     Template keys like {PlayerName} in objectives will be replaced with the corresponding value.
        /// </summary>
        /// <param name="templateKeys">Dictionary of key-value pairs to update.</param>
        /// <returns>True if the message was sent; false if the connection is not ready.</returns>
        public bool UpdateTemplateKeys(Dictionary<string, string> templateKeys);
    }
}
