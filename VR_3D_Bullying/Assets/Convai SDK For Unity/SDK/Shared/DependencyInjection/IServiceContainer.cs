using System;

namespace Convai.Shared.DependencyInjection
{
    /// <summary>
    ///     Dependency injection container for managing service lifetimes and dependencies.
    /// </summary>
    /// <remarks>
    ///     The service container provides:
    ///     - Service registration (Singleton, Transient)
    ///     - Service resolution with constructor injection
    ///     - Lifecycle management (Initialize, Dispose)
    ///     - Thread-safe singleton creation
    ///     Usage Example:
    ///     <code>
    /// 
    /// IServiceContainer container = new ServiceContainer();
    /// 
    /// 
    /// container.Register(ServiceDescriptor.Singleton&lt;ILogger, UnityLogger&gt;());
    /// container.Register(ServiceDescriptor.Singleton&lt;IEventHub, EventHub&gt;());
    /// container.Register(ServiceDescriptor.Transient&lt;ICommand, MyCommand&gt;());
    /// 
    /// 
    /// ILogger logger = container.Get&lt;ILogger&gt;();
    /// IEventHub eventHub = container.Get&lt;IEventHub&gt;();
    /// 
    /// 
    /// if (container.IsRegistered&lt;ILogger&gt;())
    /// {
    /// 
    /// }
    /// 
    /// 
    /// container.Dispose();
    /// </code>
    ///     Design Principles:
    ///     - **Lightweight**: Simple, focused implementation without reflection-heavy magic
    ///     - **Explicit**: Clear registration and resolution
    ///     - **Constructor Injection**: Only constructor injection (no property/method injection)
    ///     - **Thread-Safe**: Singleton creation is thread-safe
    ///     - **Lifecycle Management**: Automatic disposal of IDisposable singletons
    /// </remarks>
    public interface IServiceContainer : IDisposable
    {
        /// <summary>
        ///     Registers a service with the container.
        /// </summary>
        /// <param name="descriptor">Service descriptor containing registration information</param>
        /// <exception cref="ArgumentNullException">Thrown if descriptor is null</exception>
        /// <exception cref="InvalidOperationException">Thrown if service is already registered</exception>
        public void Register(ServiceDescriptor descriptor);

        /// <summary>
        ///     Resolves a service of the specified type.
        /// </summary>
        /// <typeparam name="TService">The service type to resolve</typeparam>
        /// <returns>An instance of the service</returns>
        /// <exception cref="InvalidOperationException">Thrown if service is not registered</exception>
        /// <remarks>
        ///     Resolution behavior depends on the service lifetime:
        ///     - **Singleton**: Returns the same instance on every call (thread-safe creation)
        ///     - **Transient**: Returns a new instance on every call
        ///     Constructor injection is performed automatically:
        ///     - Container resolves all constructor parameters
        ///     - Uses the constructor with the most parameters that can be satisfied
        ///     - Throws if dependencies cannot be resolved
        /// </remarks>
        public TService Get<TService>();

        /// <summary>
        ///     Attempts to resolve a service of the specified type.
        /// </summary>
        /// <typeparam name="TService">The service type to resolve</typeparam>
        /// <param name="service">The resolved service, or default if not registered</param>
        /// <returns>True if the service was resolved; false otherwise</returns>
        public bool TryGet<TService>(out TService service);

        /// <summary>
        ///     Checks if a service of the specified type is registered.
        /// </summary>
        /// <typeparam name="TService">The service type to check</typeparam>
        /// <returns>True if the service is registered; false otherwise</returns>
        public bool IsRegistered<TService>();

        /// <summary>
        ///     Initializes all registered singleton services eagerly.
        /// </summary>
        /// <remarks>
        ///     By default, singletons are created lazily on first request.
        ///     Call this method to create them all upfront for early error detection.
        /// </remarks>
        public void Initialize();

        /// <summary>
        ///     Disposes the container and all disposable singleton services.
        /// </summary>
        public new void Dispose();
    }
}
