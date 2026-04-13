#if UNITY_WEBGL && !UNITY_EDITOR
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Bootstrap;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Logging;
using Convai.Shared.DependencyInjection;
using UnityEngine;
using UnityEngine.Scripting;

namespace Convai.Infrastructure.Networking.WebGL
{
    [Preserve]
    internal sealed class WebGLNetworkingRegistrar : IPlatformNetworkingRegistrar
    {
        private static readonly object Lock = new();
        private static WebGLCoroutineRunner _coroutineRunner;
        private static IRealtimeTransport _transportSingleton;

        public string Id => "webgl";
        public int Priority => 200;

        public bool SupportsCurrentEnvironment()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        public void RegisterServices()
        {
            RealtimeTransportFactory.RegisterFactory(CreateTransport);

            if (!ConvaiServiceLocator.IsRegistered<IMicrophoneSourceFactory>())
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IMicrophoneSourceFactory>(static _ => new WebGLMicrophoneSourceFactory()));
                LogDebug("[WebGLNetworkingRegistrar] Registered IMicrophoneSourceFactory.");
            }

            if (!ConvaiServiceLocator.IsRegistered<IVideoSourceFactory>())
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IVideoSourceFactory>(static _ => new WebGLVideoSourceFactory()));
                LogDebug("[WebGLNetworkingRegistrar] Registered IVideoSourceFactory.");
            }

            if (!ConvaiServiceLocator.IsRegistered<IAudioStreamFactory>())
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IAudioStreamFactory>(static _ => new WebGLAudioStreamFactory()));
                LogDebug("[WebGLNetworkingRegistrar] Registered IAudioStreamFactory.");
            }

            if (!ConvaiServiceLocator.IsRegistered<IConvaiRoomControllerFactory>())
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IConvaiRoomControllerFactory>(static _ => new WebGLRoomControllerFactory()));
                LogDebug("[WebGLNetworkingRegistrar] Registered IConvaiRoomControllerFactory.");
            }

            if (!ConvaiServiceLocator.IsRegistered<IRealtimeTransportAccessor>())
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IRealtimeTransportAccessor>(static _ => new WebGLRealtimeTransportAccessor()));
                LogDebug("[WebGLNetworkingRegistrar] Registered IRealtimeTransportAccessor.");
            }
        }

        private static IRealtimeTransport CreateTransport()
        {
            if (_transportSingleton != null) return _transportSingleton;

            lock (Lock)
            {
                if (_transportSingleton != null) return _transportSingleton;

                MonoBehaviour coroutineRunner = GetOrCreateCoroutineRunner();
                _transportSingleton = new WebGLRealtimeTransport(coroutineRunner);
                LogDebug("[WebGLNetworkingRegistrar] Created WebGL transport singleton.");
                return _transportSingleton;
            }
        }

        private static MonoBehaviour GetOrCreateCoroutineRunner()
        {
            if (_coroutineRunner != null) return _coroutineRunner;

            lock (Lock)
            {
                if (_coroutineRunner != null) return _coroutineRunner;

                GameObject runnerObject = new GameObject("[Convai] WebGL Coroutine Runner");
                runnerObject.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(runnerObject);

                _coroutineRunner = runnerObject.AddComponent<WebGLCoroutineRunner>();
                LogDebug("[WebGLNetworkingRegistrar] Created WebGL coroutine runner.");
                return _coroutineRunner;
            }
        }

        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        [System.Diagnostics.Conditional("CONVAI_DEBUG_LOGGING")]
        private static void LogDebug(string message)
        {
            ConvaiLogger.Debug(message, LogCategory.Transport);
        }
    }
}
#endif