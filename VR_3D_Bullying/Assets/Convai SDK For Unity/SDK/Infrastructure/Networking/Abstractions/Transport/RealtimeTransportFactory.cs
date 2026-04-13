using System;

namespace Convai.Infrastructure.Networking.Transport
{
    /// <summary>
    ///     Static factory utilities and registration point for platform-specific transport implementations.
    /// </summary>
    /// <remarks>
    ///     Platform-specific assemblies should register their factory delegate during initialization
    ///     using <see cref="RegisterFactory" />. This allows the abstractions layer to remain
    ///     platform-agnostic while enabling runtime factory resolution.
    /// </remarks>
    public static class RealtimeTransportFactory
    {
        private static Func<IRealtimeTransport> _factoryDelegate;

        /// <summary>
        ///     Gets whether a factory has been registered.
        /// </summary>
        public static bool IsFactoryRegistered { get; private set; }

        /// <summary>
        ///     Gets the current platform based on compile-time defines.
        /// </summary>
        public static TransportPlatform CurrentPlatform
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return TransportPlatform.WebGL;
#elif UNITY_IOS || UNITY_ANDROID
                return TransportPlatform.Mobile;
#else
                return TransportPlatform.Desktop;
#endif
            }
        }

        /// <summary>
        ///     Returns true if running on WebGL platform.
        /// </summary>
        public static bool IsWebGL
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        ///     Returns true if running on a mobile platform.
        /// </summary>
        public static bool IsMobile
        {
            get
            {
#if UNITY_IOS || UNITY_ANDROID
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        ///     Registers a factory delegate for creating transport instances.
        ///     Should be called by platform-specific initialization code.
        /// </summary>
        /// <param name="factory">Factory function that creates IRealtimeTransport instances.</param>
        /// <exception cref="ArgumentNullException">Thrown if factory is null.</exception>
        public static void RegisterFactory(Func<IRealtimeTransport> factory)
        {
            _factoryDelegate = factory ?? throw new ArgumentNullException(nameof(factory));
            IsFactoryRegistered = true;
        }

        /// <summary>
        ///     Clears the registered factory. Useful for testing or reinitialization.
        /// </summary>
        public static void ClearFactory()
        {
            _factoryDelegate = null;
            IsFactoryRegistered = false;
        }

        /// <summary>
        ///     Creates a transport using the registered factory.
        /// </summary>
        /// <returns>A new IRealtimeTransport instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if no factory has been registered.</exception>
        public static IRealtimeTransport Create()
        {
            if (!IsFactoryRegistered || _factoryDelegate == null)
            {
                throw new InvalidOperationException(
                    "No transport factory has been registered. " +
                    "Call RealtimeTransportFactory.RegisterFactory() during platform initialization.");
            }

            return _factoryDelegate();
        }

        /// <summary>
        ///     Gets the capabilities for the current platform.
        /// </summary>
        public static TransportCapabilities GetCapabilities()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return TransportCapabilities.WebGL();
#elif UNITY_IOS || UNITY_ANDROID
            return TransportCapabilities.Native(isMobile: true);
#else
            return TransportCapabilities.Native();
#endif
        }
    }
}
