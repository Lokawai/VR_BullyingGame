using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Room;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Default Unity-layer implementation of <see cref="IConvaiAudioManager" />.
    ///     This implementation composes higher-level room services:
    ///     - <see cref="IConvaiRoomConnectionService" /> for room access
    ///     - <see cref="IConvaiRoomAudioService" /> for microphone/Character mute state
    ///     It is designed to be safe when constructed early: if the room is not yet
    ///     connected, microphone publish/unpublish calls will log and no-op.
    ///     Uses platform-agnostic abstractions for cross-platform compatibility.
    /// </summary>
    /// <remarks>
    ///     This class is registered via factory in ConvaiServiceBootstrap.RegisterAudioManager().
    ///     No [Preserve] attribute needed - direct typed registration prevents IL2CPP stripping.
    /// </remarks>
    internal sealed class DefaultAudioManager : IConvaiAudioManager
    {
        private readonly IConvaiRoomAudioService _audioService;
        private readonly IConvaiRoomConnectionService _connectionService;
        private readonly ILogger _logger;

        public DefaultAudioManager(
            IConvaiRoomConnectionService connectionService,
            IConvaiRoomAudioService audioService,
            ILogger logger = null)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _logger = logger;
        }

        public event Action<bool> OnMicMuteChanged;

        public bool IsMicMuted => _audioService.IsMicMuted;

        /// <summary>
        ///     Publishes a microphone source to the Convai room using platform-agnostic types (async).
        /// </summary>
        public async Task<ILocalAudioTrack> PublishMicrophoneAsync(
            IMicrophoneSource source,
            AudioPublishOptions options,
            CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                _logger?.Error("[DefaultAudioManager] Microphone source was null.");
                return null;
            }

            // Get room facade
            IRoomFacade roomFacade = _connectionService.CurrentRoom;
            if (roomFacade?.LocalParticipant == null)
            {
                _logger?.Debug(
                    "[DefaultAudioManager] Room or LocalParticipant not available; skipping microphone publish.");
                return null;
            }

            try
            {
                // Delegate to the platform-agnostic local participant
                // The implementation (Native/WebGL) handles the platform-specific details
                return await roomFacade.LocalParticipant
                    .PublishAudioTrackAsync(source, options, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error($"[DefaultAudioManager] Failed to publish microphone track: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        ///     Unpublishes the provided local track from the active room (async).
        /// </summary>
        public async Task UnpublishTrackAsync(ILocalTrack track, CancellationToken cancellationToken = default)
        {
            if (track == null) return;

            IRoomFacade roomFacade = _connectionService.CurrentRoom;
            if (roomFacade?.LocalParticipant == null)
            {
                _logger?.Debug("[DefaultAudioManager] Room not ready; skip unpublish.");
                return;
            }

            try
            {
                // Delegate to the platform-agnostic local participant
                await roomFacade.LocalParticipant
                    .UnpublishTrackAsync(track, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error($"[DefaultAudioManager] Failed to unpublish track: {ex.Message}");
            }
        }

        public void SetMicMuted(bool muted)
        {
            bool previous = _audioService.IsMicMuted;
            _audioService.SetMicMuted(muted);

            if (previous != muted) OnMicMuteChanged?.Invoke(muted);
        }

        public void MuteCharacter(string characterId, bool muted)
        {
            if (string.IsNullOrWhiteSpace(characterId)) return;

            bool success = _audioService.SetCharacterMuted(characterId, muted);
            if (!success)
                _logger?.Debug($"[DefaultAudioManager] Failed to set mute state for Character '{characterId}'.");
        }
    }
}
