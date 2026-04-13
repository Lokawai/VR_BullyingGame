namespace Convai.Infrastructure.Networking.Transport
{
    public interface IRealtimeTransportAccessor
    {
        public IRealtimeTransport Transport { get; }
    }
}
