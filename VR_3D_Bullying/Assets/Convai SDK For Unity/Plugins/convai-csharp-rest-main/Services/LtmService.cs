#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.RestAPI.Internal;
using Convai.RestAPI.Transport;
using Newtonsoft.Json;

namespace Convai.RestAPI.Services
{
    /// <summary>
    /// Service for Long-Term Memory (LTM) related API operations.
    /// </summary>
    public sealed class LtmService : ConvaiServiceBase
    {
        private const string NewSpeakerEndpoint = "user/speaker/new";
        private const string SpeakerListEndpoint = "user/speaker/list";
        private const string DeleteSpeakerEndpoint = "user/speaker/delete";
        private const string EndUsersListEndpoint = "user/end-users/list";
        private const string EndUsersDeleteEndpoint = "user/end-users/delete";
        private const string CharacterGetEndpoint = "character/get";
        private const string CharacterUpdateEndpoint = "character/update";

        internal LtmService(ConvaiRestClientOptions options, IConvaiHttpTransport transport)
            : base(options, transport)
        {
        }

        /// <summary>
        /// Creates a new speaker ID for long-term memory.
        /// </summary>
        /// <param name="playerName">The name of the player/speaker.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The created speaker ID.</returns>
        public async Task<string> CreateSpeakerAsync(
            string playerName,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, string>
            {
                { "name", playerName }
            };

            var result = await PostAsync<CreateSpeakerIdResult>(
                NewSpeakerEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return result.SpeakerId;
        }

        /// <summary>
        /// Gets the list of all speaker IDs.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of speaker ID details.</returns>
        public async Task<IReadOnlyList<SpeakerIDDetails>> GetSpeakersAsync(
            CancellationToken cancellationToken = default)
        {
            return await PostAsync<List<SpeakerIDDetails>>(
                SpeakerListEndpoint,
                new Dictionary<string, string>(),
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes a speaker ID.
        /// </summary>
        /// <param name="speakerId">The speaker ID to delete.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task DeleteSpeakerAsync(
            string speakerId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, string>
            {
                { "speakerId", speakerId }
            };

            await PostVoidAsync(
                DeleteSpeakerEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the list of end users (modern speakers with end_user_id).
        /// </summary>
        /// <param name="limit">Maximum number of users to return.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The end users list response.</returns>
        public async Task<EndUsersListResponse> GetEndUsersAsync(
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, object>
            {
                { "limit", limit }
            };

            return await PostAsync<EndUsersListResponse>(
                EndUsersListEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes an end user.
        /// </summary>
        /// <param name="endUserId">The end user ID to delete.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task DeleteEndUserAsync(
            string endUserId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, string>
            {
                { "end_user_id", endUserId }
            };

            await PostVoidAsync(
                EndUsersDeleteEndpoint,
                requestBody,
                useBeta: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the LTM enabled status for a character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if LTM is enabled.</returns>
        public async Task<bool> GetStatusAsync(
            string characterId,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new Dictionary<string, string>
            {
                { "charID", characterId }
            };

            var result = await PostAsync<CharacterDetails>(
                CharacterGetEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return result.MemorySettings?.IsEnabled ?? false;
        }

        /// <summary>
        /// Updates the LTM enabled status for a character.
        /// </summary>
        /// <param name="characterId">The character ID.</param>
        /// <param name="enabled">Whether to enable or disable LTM.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task SetStatusAsync(
            string characterId,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new CharacterUpdateRequest(characterId, enabled);

            await PostVoidAsync(
                CharacterUpdateEndpoint,
                requestBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // Internal request model for speaker creation response
        private sealed class CreateSpeakerIdResult
        {
            [JsonProperty("speaker_id")]
            public string SpeakerId { get; set; } = string.Empty;
        }
    }
}
