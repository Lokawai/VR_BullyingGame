using System;
using System.Globalization;
using Convai.Infrastructure.Networking.Models;
using Convai.RestAPI;
using Convai.RestAPI.Services;
using Convai.Shared.Types;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Builds the canonical room-connect request shared by native and WebGL transports.
    /// </summary>
    internal static class RoomConnectionRequestFactory
    {
        internal static RoomConnectionRequest Create(
            string characterId,
            string connectionType,
            string llmProvider,
            string coreServerUrl,
            string characterSessionId,
            string endUserId,
            string videoTrackName,
            RoomEmotionConfig emotionConfig,
            RoomJoinOptions joinOptions,
            in LipSyncTransportOptions lipSyncTransportOptions)
        {
            var roomRequest = new RoomConnectionRequest
            {
                CharacterId = characterId,
                Transport = "livekit",
                ConnectionType = connectionType,
                LlmProvider = llmProvider,
                CoreServiceUrl = coreServerUrl,
                CharacterSessionId = string.IsNullOrWhiteSpace(characterSessionId) ? null : characterSessionId,
                EndUserId = string.IsNullOrWhiteSpace(endUserId) ? null : endUserId.Trim(),
                TurnDetectionConfig = TurnDetectionConfig.CreateDefault(),
                EmotionConfig = emotionConfig
            };

            ApplyVideoTrackName(roomRequest, connectionType, videoTrackName);
            ApplyJoinOptions(roomRequest, joinOptions);
            RoomConnectionRequestLipSyncMapper.Apply(roomRequest, lipSyncTransportOptions);
            return roomRequest;
        }

        private static void ApplyVideoTrackName(
            RoomConnectionRequest roomRequest,
            string connectionType,
            string videoTrackName)
        {
            if (!string.Equals(connectionType, "video", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(videoTrackName))
                return;

            roomRequest.VideoTrackName = videoTrackName.Trim();
        }

        private static void ApplyJoinOptions(RoomConnectionRequest roomRequest, RoomJoinOptions joinOptions)
        {
            if (joinOptions != null && joinOptions.IsJoinRequest)
            {
                roomRequest.RoomName = joinOptions.RoomName;
                roomRequest.Mode = "join";
                roomRequest.SpawnAgent = joinOptions.SpawnAgent;
                if (joinOptions.MaxNumParticipants.HasValue)
                {
                    roomRequest.MaxNumParticipants =
                        joinOptions.MaxNumParticipants.Value.ToString(CultureInfo.InvariantCulture);
                }

                return;
            }

            roomRequest.Mode = "create";
            roomRequest.SpawnAgent = true;
        }
    }
}
