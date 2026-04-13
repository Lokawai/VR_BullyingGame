using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Infrastructure.Networking.Models;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Platform-agnostic interface for room connection and management.
    ///     Provides a unified API for both native (LiveKit) and WebGL implementations.
    /// </summary>
    /// <remarks>
    ///     This interface abstracts the room connection lifecycle including:
    ///     - Connection/disconnection management
    ///     - Audio control (microphone muting, character audio)
    ///     - Session management
    ///     - Event notifications for connection state changes
    ///     Implementations:
    ///     - Native: NativeRoomController (direct LiveKit integration)
    ///     - WebGL: WebGLRoomController using IRealtimeTransport
    /// </remarks>
    public interface IConvaiRoomController : IDisposable
    {
        #region State Properties

        /// <summary>
        ///     Gets whether room connection details have been successfully retrieved.
        /// </summary>
        public bool HasRoomDetails { get; }

        /// <summary>
        ///     Gets whether currently connected to the room.
        /// </summary>
        public bool IsConnectedToRoom { get; }

        /// <summary>
        ///     Gets whether the microphone is currently muted.
        /// </summary>
        public bool IsMicMuted { get; }

        /// <summary>
        ///     Gets the session ID for the current connection.
        /// </summary>
        public string SessionID { get; }

        /// <summary>
        ///     Gets the character session ID for the current connection.
        /// </summary>
        public string CharacterSessionID { get; }

        /// <summary>
        ///     Gets the room name for the current connection.
        /// </summary>
        public string RoomName { get; }

        /// <summary>
        ///     Gets the room URL for the current connection.
        /// </summary>
        public string RoomURL { get; }

        /// <summary>
        ///     Gets the authentication token for the current connection.
        /// </summary>
        public string Token { get; }

        /// <summary>
        ///     Gets the resolved speaker ID for the local participant.
        /// </summary>
        public string ResolvedSpeakerId { get; }

        /// <summary>
        ///     Gets the active RTVI handler for the current room.
        /// </summary>
        public RTVIHandler RTVIHandler { get; }

        /// <summary>
        ///     Gets the current room facade for room-backed runtime services.
        ///     Null until a room is available for the active platform/runtime path.
        /// </summary>
        public IRoomFacade CurrentRoom { get; }

        #endregion

        #region Connection Methods

        /// <summary>
        ///     Initializes the room connection process.
        /// </summary>
        /// <param name="connectionType">Connection type (audio/video).</param>
        /// <param name="llmProvider">LLM provider to use.</param>
        /// <param name="coreServerUrl">Core server URL.</param>
        /// <param name="characterId">Character ID to connect to.</param>
        /// <param name="storedSessionId">Optional stored session ID for resumption.</param>
        /// <param name="enableSessionResume">Whether to enable session resume.</param>
        /// <returns>True if initialization succeeded.</returns>
        public Task<bool> InitializeAsync(
            string connectionType,
            string llmProvider,
            string coreServerUrl,
            string characterId,
            string storedSessionId,
            bool enableSessionResume);

        /// <summary>
        ///     Initializes the room connection process with join options.
        /// </summary>
        /// <param name="connectionType">Connection type (audio/video).</param>
        /// <param name="llmProvider">LLM provider to use.</param>
        /// <param name="coreServerUrl">Core server URL.</param>
        /// <param name="characterId">Character ID to connect to.</param>
        /// <param name="storedSessionId">Optional stored session ID for resumption.</param>
        /// <param name="enableSessionResume">Whether to enable session resume.</param>
        /// <param name="joinOptions">Optional room join options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if initialization succeeded.</returns>
        public Task<bool> InitializeAsync(
            string connectionType,
            string llmProvider,
            string coreServerUrl,
            string characterId,
            string storedSessionId,
            bool enableSessionResume,
            RoomJoinOptions joinOptions,
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Disconnects from the current room.
        /// </summary>
        public void DisconnectFromRoom();

        /// <summary>
        ///     Disconnects from the current room asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task DisconnectFromRoomAsync(CancellationToken cancellationToken = default);

        #endregion

        #region Audio Control

        /// <summary>
        ///     Sets the microphone muted state.
        /// </summary>
        /// <param name="mute">True to mute, false to unmute.</param>
        public void SetMicMuted(bool mute);

        /// <summary>
        ///     Toggles the microphone muted state.
        /// </summary>
        public void ToggleMicMute();

        /// <summary>
        ///     Sets the audio muted state for a specific character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <param name="mute">True to mute, false to unmute.</param>
        /// <returns>True if the operation succeeded.</returns>
        public bool SetCharacterAudioMuted(string characterId, bool mute);

        /// <summary>
        ///     Mutes audio for a specific character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <returns>True if the operation succeeded.</returns>
        public bool MuteCharacter(string characterId);

        /// <summary>
        ///     Unmutes audio for a specific character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <returns>True if the operation succeeded.</returns>
        public bool UnmuteCharacter(string characterId);

        /// <summary>
        ///     Gets whether a character's audio is currently muted.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <returns>True if the character is muted.</returns>
        public bool IsCharacterAudioMuted(string characterId);

        /// <summary>
        ///     Sets the audio subscription policy for remote participants.
        /// </summary>
        /// <param name="policy">Function that returns true if the character should receive audio.</param>
        public void SetAudioSubscriptionPolicy(Func<string, bool> policy);

        /// <summary>
        ///     Applies remote audio preference for a specific character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <param name="enabled">Whether audio should be enabled.</param>
        public void ApplyRemoteAudioPreference(string characterId, bool enabled);

        #endregion

        #region Session Management

        /// <summary>
        ///     Gets the stored session ID for a character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <returns>The stored session ID, or null if none exists.</returns>
        public string GetStoredSessionId(string characterId);

        /// <summary>
        ///     Clears the stored session ID for a character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        public void ClearStoredSessionId(string characterId);

        /// <summary>
        ///     Clears all stored session IDs.
        /// </summary>
        public void ClearAllStoredSessionIds();

        /// <summary>
        ///     Gets the current character session ID.
        /// </summary>
        /// <returns>The current character session ID.</returns>
        public string GetCurrentCharacterSessionId();

        #endregion

        #region Events

        /// <summary>
        ///     Raised when room connection succeeds.
        /// </summary>
        public event Action OnRoomConnectionSuccessful;

        /// <summary>
        ///     Raised when room connection fails.
        /// </summary>
        public event Action OnRoomConnectionFailed;

        /// <summary>
        ///     Raised when microphone mute state changes.
        ///     Parameter: new mute state (true = muted).
        /// </summary>
        public event Action<bool> OnMicMuteChanged;

        /// <summary>
        ///     Raised when the room is reconnecting.
        /// </summary>
        public event Action OnRoomReconnecting;

        /// <summary>
        ///     Raised when the room has reconnected.
        /// </summary>
        public event Action OnRoomReconnected;

        /// <summary>
        ///     Raised when the room is disconnected unexpectedly and controller-owned
        ///     teardown/reset has completed.
        /// </summary>
        public event Action OnUnexpectedRoomDisconnected;

        /// <summary>
        ///     Raised when a remote audio track is subscribed.
        ///     Parameters: (audioTrack, participantSid, characterId).
        /// </summary>
        public event Action<IRemoteAudioTrack, string, string> OnRemoteAudioTrackSubscribed;

        /// <summary>
        ///     Raised when a remote audio track is unsubscribed.
        ///     Parameters: (participantSid, characterId).
        /// </summary>
        public event Action<string, string> OnRemoteAudioTrackUnsubscribed;

        #endregion
    }
}
