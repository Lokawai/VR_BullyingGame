using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Infrastructure.Networking;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Manages microphone publishing and Character audio state within the Convai room.
    ///     Uses platform-agnostic abstractions for cross-platform compatibility.
    /// </summary>
    public interface IConvaiAudioManager
    {
        /// <summary>
        ///     Indicates whether the microphone is currently muted.
        /// </summary>
        public bool IsMicMuted { get; }

        /// <summary>
        ///     Publishes a microphone source to the Convai room using the supplied options (async).
        /// </summary>
        /// <param name="source">Microphone audio source to publish.</param>
        /// <param name="options">Publish options controlling encoding and track metadata.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The created local audio track instance.</returns>
        public Task<ILocalAudioTrack> PublishMicrophoneAsync(
            IMicrophoneSource source,
            AudioPublishOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Unpublishes the provided local track from the active room (async).
        /// </summary>
        /// <param name="track">Track to unpublish.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task UnpublishTrackAsync(
            ILocalTrack track,
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Sets the microphone mute state for the local participant.
        /// </summary>
        /// <param name="muted">True to mute; otherwise false.</param>
        public void SetMicMuted(bool muted);

        /// <summary>
        ///     Mutes or unmutes the specified Character by character identifier.
        /// </summary>
        /// <param name="characterId">The Convai Character identifier.</param>
        /// <param name="muted">True to mute; false to unmute.</param>
        public void MuteCharacter(string characterId, bool muted);

        /// <summary>
        ///     Raised whenever the microphone mute state changes.
        /// </summary>
        public event Action<bool> OnMicMuteChanged;
    }
}
