namespace Convai.Domain.Abstractions
{
    /// <summary>
    ///     Abstraction for SDK settings access.
    ///     Implemented by Unity layer (ConvaiSettings) and registered during bootstrap.
    /// </summary>
    /// <remarks>
    ///     This interface allows the Application layer (ConvaiRoomSession) to access settings
    ///     without directly referencing the Unity layer, avoiding circular assembly references.
    ///     The Unity layer registers an adapter that wraps ConvaiSettings.Instance.
    /// </remarks>
    public interface IConvaiSettingsProvider
    {
        /// <summary>
        ///     Gets the API key for Convai services.
        /// </summary>
        public string ApiKey { get; }

        /// <summary>
        ///     Gets the server URL for Convai services.
        /// </summary>
        public string ServerUrl { get; }

        /// <summary>
        ///     Gets the player name for the current session.
        /// </summary>
        public string PlayerName { get; }

        /// <summary>
        ///     Gets a value indicating whether the API key is configured.
        /// </summary>
        public bool HasApiKey { get; }
    }
}
