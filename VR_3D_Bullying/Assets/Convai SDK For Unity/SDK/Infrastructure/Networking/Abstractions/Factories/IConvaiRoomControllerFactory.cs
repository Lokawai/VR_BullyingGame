using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Factory interface for creating IConvaiRoomController instances.
    ///     Platform-specific implementations provide the appropriate controller type.
    /// </summary>
    public interface IConvaiRoomControllerFactory
    {
        /// <summary>
        ///     Creates an IConvaiRoomController instance with the specified dependencies.
        /// </summary>
        /// <param name="characterRegistry">Registry for character management.</param>
        /// <param name="playerSession">Player session for transcription events.</param>
        /// <param name="config">Configuration provider.</param>
        /// <param name="dispatcher">Main thread dispatcher.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="eventHub">Event hub for domain events.</param>
        /// <param name="sectionNameResolver">Optional narrative section name resolver.</param>
        /// <returns>A new IConvaiRoomController instance.</returns>
        public IConvaiRoomController Create(
            ICharacterRegistry characterRegistry,
            IPlayerSession playerSession,
            IConfigurationProvider config,
            IMainThreadDispatcher dispatcher,
            ILogger logger,
            IEventHub eventHub,
            INarrativeSectionNameResolver sectionNameResolver = null);
    }
}
