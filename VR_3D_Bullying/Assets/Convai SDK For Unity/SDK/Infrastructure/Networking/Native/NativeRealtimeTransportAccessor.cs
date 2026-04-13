using System;
using Convai.Infrastructure.Networking.Transport;

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeRealtimeTransportAccessor : IRealtimeTransportAccessor
    {
        private readonly Func<IRealtimeTransport> _createTransport;

        internal NativeRealtimeTransportAccessor(Func<IRealtimeTransport> createTransport = null)
        {
            _createTransport = createTransport ?? RealtimeTransportFactory.Create;
        }

        public IRealtimeTransport Transport => _createTransport.Invoke();
    }
}
