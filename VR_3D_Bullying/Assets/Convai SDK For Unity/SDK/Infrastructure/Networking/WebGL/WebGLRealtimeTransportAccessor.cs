using Convai.Infrastructure.Networking.Transport;

namespace Convai.Infrastructure.Networking.WebGL
{
    internal sealed class WebGLRealtimeTransportAccessor : IRealtimeTransportAccessor
    {
        public IRealtimeTransport Transport => RealtimeTransportFactory.Create();
    }
}
