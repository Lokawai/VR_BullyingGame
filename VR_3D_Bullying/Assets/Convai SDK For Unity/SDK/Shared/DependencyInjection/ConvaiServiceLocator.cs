using System;

namespace Convai.Shared.DependencyInjection
{
    /// <summary>
    ///     Static facade for accessing the global service container.
    ///     Provides a convenient static API for service resolution.
    /// </summary>
    /// <remarks>
    ///     ConvaiServiceLocator provides a convenient static API for service resolution.
    ///     It wraps the underlying ServiceContainer and provides thread-safe access to
    ///     registered services. For events, use IEventHub via dependency injection.
    ///     Usage Example:
    ///     <code>
    /// 
    /// ConvaiServiceLocator.Initialize();
    /// 
    /// 
    /// ConvaiServiceLocator.Register(ServiceDescriptor.Singleton&lt;ILogger, UnityLogger&gt;());
    /// ConvaiServiceLocator.Register(ServiceDescriptor.Singleton&lt;IEventHub, EventHub&gt;());
    /// 
    /// 
    /// ILogger logger = ConvaiServiceLocator.Get&lt;ILogger&gt;();
    /// IEventHub eventHub = ConvaiServiceLocator.Get&lt;IEventHub&gt;();
    /// 
    /// 
    /// if (ConvaiServiceLocator.IsRegistered&lt;ILogger&gt;())
    /// {
    /// 
    /// }
    /// 
    /// 
    /// ConvaiServiceLocator.Shutdown();
    /// </code>
    ///     Design Principles:
    ///     - **Static Facade**: Convenient static API for gameplay code
    ///     - **Lazy Initialization**: Container created on first access
    ///     - **Thread-Safe**: All operations are thread-safe
    ///     - **Explicit Lifecycle**: Initialize() and Shutdown() for clear lifecycle management
    ///     Integration with Unity:
    ///     - Call Initialize() in a Unity startup script (e.g., ConvaiManager pipeline)
    ///     - Register core services (EventHub, Logger, UnityScheduler)
    ///     - Call Shutdown() in OnApplicationQuit to dispose services
    /// </remarks>
    public static class ConvaiServiceLocator
    {
        private static readonly object _lock = new();
        private static IServiceContainer _container;

        /// <summary>
        ///     Gets whether the service locator has been initialized.
        /// </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>
        ///     Initializes the service locator with a new container.
        /// </summary>
        /// <remarks>
        ///     This method should be called once at application startup, typically in a
        ///     Unity MonoBehaviour's Awake() or Start() method.
        ///     If the service locator is already initialized, this method is a no-op.
        ///     Example:
        ///     <code>
        /// 
        /// void Awake()
        /// {
        ///     ConvaiServiceLocator.Initialize();
        /// 
        /// 
        ///     ConvaiServiceLocator.Register(ServiceDescriptor.Singleton&lt;ILogger, UnityLogger&gt;());
        ///     ConvaiServiceLocator.Register(ServiceDescriptor.Singleton&lt;IEventHub, EventHub&gt;());
        /// }
        /// </code>
        /// </remarks>
        public static void Initialize()
        {
            if (IsInitialized)
                return;

            lock (_lock)
            {
                if (IsInitialized)
                    return;

                _container = new ServiceContainer();
                IsInitialized = true;
            }
        }

        /// <summary>
        ///     Initializes the service locator with a custom container.
        /// </summary>
        /// <param name="container">The service container to use</param>
        /// <remarks>
        ///     This overload allows injecting a custom container implementation,
        ///     useful for testing or advanced scenarios.
        ///     Example:
        ///     <code>
        /// 
        /// IServiceContainer mockContainer = new MockServiceContainer();
        /// ConvaiServiceLocator.Initialize(mockContainer);
        /// </code>
        /// </remarks>
        public static void Initialize(IServiceContainer container)
        {
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            if (IsInitialized)
                throw new InvalidOperationException("Service locator is already initialized. Call Shutdown() first.");

            lock (_lock)
            {
                if (IsInitialized)
                {
                    throw new InvalidOperationException(
                        "Service locator is already initialized. Call Shutdown() first.");
                }

                _container = container;
                IsInitialized = true;
            }
        }

        /// <summary>
        ///     Shuts down the service locator and disposes the container.
        /// </summary>
        /// <remarks>
        ///     This method should be called once at application shutdown, typically in a
        ///     Unity MonoBehaviour's OnApplicationQuit() or OnDestroy() method.
        ///     After calling Shutdown(), the service locator can be re-initialized with Initialize().
        ///     Example:
        ///     <code>
        /// 
        /// void OnApplicationQuit()
        /// {
        ///     ConvaiServiceLocator.Shutdown();
        /// }
        /// </code>
        /// </remarks>
        public static void Shutdown()
        {
            if (!IsInitialized)
                return;

            lock (_lock)
            {
                if (!IsInitialized)
                    return;

                _container?.Dispose();
                _container = null;
                IsInitialized = false;
            }
        }

        /// <summary>
        ///     Registers a service with the container.
        /// </summary>
        /// <param name="descriptor">Service descriptor containing registration information</param>
        /// <exception cref="InvalidOperationException">Thrown if service locator is not initialized</exception>
        /// <remarks>
        ///     Services must be registered before they can be resolved.
        ///     Example:
        ///     <code>
        /// ConvaiServiceLocator.Register(ServiceDescriptor.Singleton&lt;ILogger, UnityLogger&gt;());
        /// ConvaiServiceLocator.Register(ServiceDescriptor.Transient&lt;ICommand, MyCommand&gt;());
        /// </code>
        /// </remarks>
        public static void Register(ServiceDescriptor descriptor)
        {
            EnsureInitialized();
            _container.Register(descriptor);
        }

        /// <summary>
        ///     Resolves a service of the specified type.
        /// </summary>
        /// <typeparam name="TService">The service type to resolve</typeparam>
        /// <returns>An instance of the service</returns>
        /// <exception cref="InvalidOperationException">Thrown if service locator is not initialized or service is not registered</exception>
        /// <remarks>
        ///     Example:
        ///     <code>
        /// ILogger logger = ConvaiServiceLocator.Get&lt;ILogger&gt;();
        /// IEventHub eventHub = ConvaiServiceLocator.Get&lt;IEventHub&gt;();
        /// </code>
        /// </remarks>
        public static TService Get<TService>()
        {
            EnsureInitialized();
            return _container.Get<TService>();
        }

        /// <summary>
        ///     Attempts to resolve a service of the specified type.
        /// </summary>
        /// <typeparam name="TService">The service type to resolve</typeparam>
        /// <param name="service">The resolved service, or default if not registered</param>
        /// <returns>True if the service was resolved; false otherwise</returns>
        /// <remarks>
        ///     This is a safe version of Get&lt;T&gt; that doesn't throw if the service
        ///     is not registered or the service locator is not initialized.
        ///     Example:
        ///     <code>
        /// if (ConvaiServiceLocator.TryGet&lt;ILogger&gt;(out ILogger logger))
        /// {
        ///     logger.Log("Service resolved");
        /// }
        /// </code>
        /// </remarks>
        public static bool TryGet<TService>(out TService service)
        {
            service = default;

            if (!IsInitialized)
                return false;

            return _container.TryGet(out service);
        }

        /// <summary>
        ///     Checks if a service of the specified type is registered.
        /// </summary>
        /// <typeparam name="TService">The service type to check</typeparam>
        /// <returns>True if the service is registered; false otherwise</returns>
        /// <remarks>
        ///     Example:
        ///     <code>
        /// if (ConvaiServiceLocator.IsRegistered&lt;ILogger&gt;())
        /// {
        ///     ILogger logger = ConvaiServiceLocator.Get&lt;ILogger&gt;();
        /// }
        /// </code>
        /// </remarks>
        public static bool IsRegistered<TService>()
        {
            if (!IsInitialized)
                return false;

            return _container.IsRegistered<TService>();
        }

        /// <summary>
        ///     Initializes all registered singleton services.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if service locator is not initialized</exception>
        /// <remarks>
        ///     This method eagerly creates all singleton instances.
        ///     Call this after registering all services to ensure they're initialized at startup.
        ///     Example:
        ///     <code>
        /// 
        /// ConvaiServiceLocator.Register(ServiceDescriptor.Singleton&lt;ILogger, UnityLogger&gt;());
        /// ConvaiServiceLocator.Register(ServiceDescriptor.Singleton&lt;IEventHub, EventHub&gt;());
        /// 
        /// 
        /// ConvaiServiceLocator.InitializeServices();
        /// </code>
        /// </remarks>
        public static void InitializeServices()
        {
            EnsureInitialized();
            _container.Initialize();
        }

        /// <summary>
        ///     Gets the underlying service container.
        /// </summary>
        /// <returns>The service container</returns>
        /// <exception cref="InvalidOperationException">Thrown if service locator is not initialized</exception>
        /// <remarks>
        ///     This method is provided for advanced scenarios where direct container access is needed.
        ///     Most code should use the static methods instead.
        /// </remarks>
        public static IServiceContainer GetContainer()
        {
            EnsureInitialized();
            return _container;
        }

        private static void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Service locator is not initialized. Call ConvaiServiceLocator.Initialize() first.");
            }
        }
    }
}
