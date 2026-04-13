using System;
using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Transport;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Factory for creating <see cref="IConvaiRoomController" /> instances on native platforms.
    ///     Creates a <see cref="NativeRoomController" /> directly.
    /// </summary>
    internal sealed class NativeRoomControllerFactory : IConvaiRoomControllerFactory
    {
        private readonly Func<IRealtimeTransport> _createTransport;

        internal NativeRoomControllerFactory(
            Func<IRealtimeTransport> createTransport = null)
        {
            _createTransport = createTransport ?? RealtimeTransportFactory.Create;
        }

        /// <inheritdoc />
        public IConvaiRoomController Create(
            ICharacterRegistry characterRegistry,
            IPlayerSession playerSession,
            IConfigurationProvider config,
            IMainThreadDispatcher dispatcher,
            ILogger logger,
            IEventHub eventHub,
            INarrativeSectionNameResolver sectionNameResolver = null)
        {
            IRealtimeTransport transport = _createTransport.Invoke();
            LogTransportOnlyPath(logger);

            return new NativeRoomController(
                characterRegistry,
                playerSession,
                config,
                dispatcher,
                logger,
                eventHub,
                sectionNameResolver,
                transport);
        }

        private static void LogTransportOnlyPath(ILogger logger)
        {
            if (logger == null) return;

            logger.Info(
                "[NativeRoomControllerFactory] Native runtime uses the transport-backed room controller.",
                LogCategory.Transport);
        }
    }
}
