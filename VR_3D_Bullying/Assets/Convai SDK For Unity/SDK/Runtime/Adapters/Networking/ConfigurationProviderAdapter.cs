using System;
using Convai.Domain.Identity;
using Convai.Infrastructure.Networking;
using Convai.Shared.DependencyInjection;
using Convai.Shared.Types;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Adapter for IConfigurationProvider that wraps ConvaiSettings.
    ///     Session data is managed separately via ConvaiSessionData.
    /// </summary>
    internal class ConfigurationProviderAdapter : IConfigurationProvider
    {
        private readonly ConvaiSettings _settings;
        private string _cachedEndUserId;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConfigurationProviderAdapter" /> class.
        /// </summary>
        /// <param name="settings">Convai settings instance.</param>
        public ConfigurationProviderAdapter(ConvaiSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <inheritdoc />
        public string ApiKey => _settings.ApiKey;

        /// <inheritdoc />
        public string CoreServerUrl => _settings.ServerUrl;

        /// <inheritdoc />
        public ConvaiConnectionType ConnectionType { get; set; } = ConvaiConnectionType.Audio;

        /// <inheritdoc />
        public string VideoTrackName { get; set; } = VideoPublishOptions.Default.TrackName;

        /// <inheritdoc />
        public ConvaiLLMProvider LlmProvider { get; set; } = ConvaiLLMProvider.Dynamic;

        /// <inheritdoc />
        public ConvaiServerEndpoint ServerEndpoint { get; set; } = ConvaiServerEndpoint.Connect;

        /// <inheritdoc />
        public LipSyncTransportOptions LipSyncTransportOptions { get; set; } = LipSyncTransportOptions.Disabled;

        /// <summary>
        ///     Gets the end user ID for session identification.
        ///     Delegates to IEndUserIdProvider which handles all fallback logic internally.
        ///     The result is cached for the lifetime of this adapter instance.
        /// </summary>
        public string EndUserId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_cachedEndUserId)) return _cachedEndUserId;

                if (ConvaiServiceLocator.IsInitialized &&
                    ConvaiServiceLocator.TryGet(out IEndUserIdProvider provider))
                    _cachedEndUserId = provider.GenerateEndUserId();

                return _cachedEndUserId;
            }
        }

        /// <inheritdoc />
        public void StoreCharacterSessionId(string characterId, string sessionId) =>
            ConvaiSessionData.Instance.StoreSessionId(characterId, sessionId);

        /// <inheritdoc />
        public string GetCharacterSessionId(string characterId) => ConvaiSessionData.Instance.GetSessionId(characterId);

        /// <inheritdoc />
        public void ClearCharacterSessionId(string characterId) =>
            ConvaiSessionData.Instance.ClearSessionId(characterId);

        /// <inheritdoc />
        public void ClearAllCharacterSessionIds() => ConvaiSessionData.Instance.ClearAllSessionIds();
    }
}
