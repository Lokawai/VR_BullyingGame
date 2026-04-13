#if UNITY_WEBGL && !UNITY_EDITOR
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Bootstrap;
using Convai.Runtime.Logging;
using UnityEngine;
using UnityEngine.Scripting;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    /// Thin runtime entrypoint that announces the WebGL networking registrar before scene load.
    /// </summary>
    [Preserve]
    internal static class WebGLTransportBootstrap
    {
        /// <summary>
        /// Registers the WebGL registrar before any scene loads.
        /// </summary>
        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            PlatformNetworkingRegistrarRegistry.Register(new WebGLNetworkingRegistrar());
            LogDebug("[WebGLTransportBootstrap] Registered WebGL networking registrar.");
        }

        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        [System.Diagnostics.Conditional("CONVAI_DEBUG_LOGGING")]
        private static void LogDebug(string message)
        {
            ConvaiLogger.Debug(message, LogCategory.Transport);
        }
    }

    /// <summary>
    /// Simple MonoBehaviour that provides coroutine support for WebGL transport operations.
    /// Persists across scene changes (DontDestroyOnLoad is called when the object is created).
    /// </summary>
    internal sealed class WebGLCoroutineRunner : MonoBehaviour
    {
    }
}
#endif
