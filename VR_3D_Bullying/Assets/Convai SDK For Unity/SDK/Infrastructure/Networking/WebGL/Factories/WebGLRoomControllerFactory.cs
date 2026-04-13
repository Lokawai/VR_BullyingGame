using System;
using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Logging;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;
using LogCategory = Convai.Domain.Logging.LogCategory;
using Object = UnityEngine.Object;

namespace Convai.Infrastructure.Networking.WebGL
{
    /// <summary>
    ///     Factory for creating IConvaiRoomController instances on WebGL platforms.
    ///     Creates a WebGLRoomController using the IRealtimeTransport abstraction.
    /// </summary>
    internal sealed class WebGLRoomControllerFactory : IConvaiRoomControllerFactory
    {
        private static MonoBehaviour _coroutineRunner;
        private static readonly object _lock = new();

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
            // Get the transport from the RealtimeTransportFactory
            if (!RealtimeTransportFactory.IsFactoryRegistered)
            {
                logger?.Error("[WebGLRoomControllerFactory] RealtimeTransportFactory not registered",
                    LogCategory.Transport);
                return null;
            }

            IRealtimeTransport transport;
            try
            {
                transport = RealtimeTransportFactory.Create();
            }
            catch (Exception ex)
            {
                logger?.Error($"[WebGLRoomControllerFactory] Failed to create transport: {ex.Message}",
                    LogCategory.Transport);
                return null;
            }

            // Get or create a coroutine runner for HTTP calls
            MonoBehaviour coroutineRunner = GetOrCreateCoroutineRunner();
            if (coroutineRunner == null)
            {
                logger?.Error("[WebGLRoomControllerFactory] Failed to create coroutine runner", LogCategory.Transport);
                return null;
            }

            return new WebGLRoomController(
                characterRegistry,
                playerSession,
                config,
                dispatcher,
                logger,
                eventHub,
                transport,
                coroutineRunner,
                sectionNameResolver);
        }

        /// <summary>
        ///     Gets or creates a coroutine runner for HTTP operations.
        /// </summary>
        private static MonoBehaviour GetOrCreateCoroutineRunner()
        {
            if (_coroutineRunner != null) return _coroutineRunner;

            lock (_lock)
            {
                if (_coroutineRunner != null) return _coroutineRunner;

                // Create a dedicated GameObject for coroutine running
                var runnerObject = new GameObject("[Convai] WebGL HTTP Coroutine Runner");
                runnerObject.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(runnerObject);

                _coroutineRunner = runnerObject.AddComponent<WebGLHttpCoroutineRunner>();

                ConvaiLogger.Debug("[WebGLRoomControllerFactory] Created HTTP coroutine runner.",
                    LogCategory.Transport);

                return _coroutineRunner;
            }
        }
    }

    /// <summary>
    ///     Simple MonoBehaviour that provides coroutine support for WebGL HTTP operations.
    /// </summary>
    internal sealed class WebGLHttpCoroutineRunner : MonoBehaviour
    {
        // Intentionally empty - just provides coroutine hosting
    }
}
