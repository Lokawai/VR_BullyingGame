using Convai.Infrastructure.Networking.Bootstrap;
using Convai.Infrastructure.Networking.Connection;
using Convai.Infrastructure.Networking.Transport;
using Convai.Shared.DependencyInjection;
using UnityEngine;
using UnityEngine.Scripting;
using IConvaiLogger = Convai.Domain.Logging.ILogger;
using Object = UnityEngine.Object;

namespace Convai.Infrastructure.Networking.Native
{
    [Preserve]
    internal sealed class NativeNetworkingRegistrar : IPlatformNetworkingRegistrar
    {
        private static readonly object Lock = new();
        private static LiveKitRoomBackend _backend;
        private static IConvaiLogger _logger;
        private static GameObject _audioSourceHolder;
        private static IRealtimeTransport _transport;

        public string Id => "native";
        public int Priority => 100;

        public bool SupportsCurrentEnvironment()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            return true;
#endif
        }

        public void RegisterServices()
        {
            RealtimeTransportFactory.RegisterFactory(CreateTransport);

            if (!ConvaiServiceLocator.IsRegistered<IMicrophoneSourceFactory>())
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IMicrophoneSourceFactory>(static _ => new NativeMicrophoneSourceFactory()));
            }

            if (!ConvaiServiceLocator.IsRegistered<IVideoSourceFactory>())
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IVideoSourceFactory>(static _ => new NativeVideoSourceFactory()));
            }

            if (!ConvaiServiceLocator.IsRegistered<IAudioStreamFactory>())
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IAudioStreamFactory>(static _ => new NativeAudioStreamFactory()));
            }

            if (!ConvaiServiceLocator.IsRegistered<IConvaiRoomControllerFactory>())
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IConvaiRoomControllerFactory>(static _ => new NativeRoomControllerFactory()));
            }

            if (!ConvaiServiceLocator.IsRegistered<IRealtimeTransportAccessor>())
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IRealtimeTransportAccessor>(static _ => new NativeRealtimeTransportAccessor()));
            }
        }

        private static IRealtimeTransport CreateTransport()
        {
            EnsureInitialized();

            if (_transport != null) return _transport;

            lock (Lock)
            {
                return _transport ??= new NativeRealtimeTransport(_backend, _logger, _audioSourceHolder);
            }
        }

        private static void EnsureInitialized()
        {
            if (_backend != null) return;

            lock (Lock)
            {
                if (_backend != null) return;

                _logger = new NativeTransportFallbackLogger();
                _audioSourceHolder = CreateAudioSourceHolder();
                _backend = new LiveKitRoomBackend(_logger);
            }
        }

        private static GameObject CreateAudioSourceHolder()
        {
            var holder = new GameObject("[Convai] Native Transport Audio");
            holder.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(holder);
            return holder;
        }
    }
}