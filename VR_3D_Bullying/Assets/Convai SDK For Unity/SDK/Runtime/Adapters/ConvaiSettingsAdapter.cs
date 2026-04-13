using Convai.Domain.Abstractions;

namespace Convai.Runtime.Adapters
{
    /// <summary>
    ///     Adapter that wraps ConvaiSettings to implement IConvaiSettingsProvider.
    ///     Registered during bootstrap to allow Application layer access without reflection.
    /// </summary>
    internal sealed class ConvaiSettingsAdapter : IConvaiSettingsProvider
    {
        private readonly ConvaiSettings _settings;

        /// <summary>
        ///     Creates a new adapter wrapping the specified settings.
        /// </summary>
        /// <param name="settings">The ConvaiSettings instance to wrap.</param>
        public ConvaiSettingsAdapter(ConvaiSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        ///     Creates a new adapter wrapping ConvaiSettings.Instance.
        /// </summary>
        public ConvaiSettingsAdapter() : this(ConvaiSettings.Instance)
        {
        }

        /// <inheritdoc />
        public string ApiKey => _settings?.ApiKey ?? string.Empty;

        /// <inheritdoc />
        public string ServerUrl => _settings?.ServerUrl ?? string.Empty;

        /// <inheritdoc />
        public string PlayerName => _settings?.PlayerName ?? string.Empty;

        /// <inheritdoc />
        public bool HasApiKey => _settings?.HasApiKey ?? false;
    }
}
