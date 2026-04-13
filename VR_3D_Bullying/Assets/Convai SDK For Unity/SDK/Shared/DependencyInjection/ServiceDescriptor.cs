using System;

namespace Convai.Shared.DependencyInjection
{
    /// <summary>
    ///     Describes a service registration in the dependency injection container.
    /// </summary>
    /// <remarks>
    ///     A ServiceDescriptor contains all the information needed to create and manage
    ///     a service instance:
    ///     - ServiceType: The interface or base type being registered
    ///     - ImplementationType: The concrete type to instantiate
    ///     - Lifetime: How instances are created and shared
    ///     - Factory: Optional factory function for custom instantiation
    ///     ServiceDescriptors are created via static factory methods:
    ///     <code>
    /// 
    /// ServiceDescriptor descriptor = ServiceDescriptor.Singleton&lt;ILogger, UnityLogger&gt;();
    /// 
    /// 
    /// ServiceDescriptor descriptor = ServiceDescriptor.Singleton&lt;ILogger&gt;(container =>
    ///     new UnityLogger(container.Get&lt;IConfig&gt;())
    /// );
    /// 
    /// 
    /// ILogger logger = new UnityLogger();
    /// ServiceDescriptor descriptor = ServiceDescriptor.Singleton&lt;ILogger&gt;(logger);
    /// </code>
    /// </remarks>
    public sealed class ServiceDescriptor
    {
        /// <summary>
        ///     Creates a new ServiceDescriptor.
        /// </summary>
        private ServiceDescriptor(
            Type serviceType,
            Type implementationType,
            ServiceLifetime lifetime,
            Func<IServiceContainer, object> factory = null,
            object instance = null)
        {
            ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
            ImplementationType = implementationType;
            Lifetime = lifetime;
            Factory = factory;
            Instance = instance;
        }

        /// <summary>
        ///     The service type (interface or base class) being registered.
        /// </summary>
        public Type ServiceType { get; }

        /// <summary>
        ///     The concrete implementation type to instantiate.
        ///     Null if using a factory or instance.
        /// </summary>
        public Type ImplementationType { get; }

        /// <summary>
        ///     The lifetime of the service (Singleton or Transient).
        /// </summary>
        public ServiceLifetime Lifetime { get; }

        /// <summary>
        ///     Optional factory function for creating instances.
        ///     Used when custom instantiation logic is needed.
        /// </summary>
        public Func<IServiceContainer, object> Factory { get; }

        /// <summary>
        ///     Optional pre-created instance (for singleton registrations).
        /// </summary>
        public object Instance { get; }

        /// <summary>
        ///     Creates a singleton service descriptor with type registration.
        /// </summary>
        public static ServiceDescriptor Singleton<TService, TImplementation>()
            where TImplementation : TService
        {
            return new ServiceDescriptor(
                typeof(TService),
                typeof(TImplementation),
                ServiceLifetime.Singleton
            );
        }

        /// <summary>
        ///     Creates a singleton service descriptor with a factory function.
        /// </summary>
        public static ServiceDescriptor Singleton<TService>(Func<IServiceContainer, TService> factory)
        {
            return new ServiceDescriptor(
                typeof(TService),
                null,
                ServiceLifetime.Singleton,
                container => factory(container)
            );
        }

        /// <summary>
        ///     Creates a singleton service descriptor with a pre-created instance.
        /// </summary>
        public static ServiceDescriptor Singleton<TService>(TService instance)
        {
            return new ServiceDescriptor(
                typeof(TService),
                null,
                ServiceLifetime.Singleton,
                instance: instance
            );
        }

        /// <summary>
        ///     Creates a transient service descriptor with type registration.
        /// </summary>
        public static ServiceDescriptor Transient<TService, TImplementation>()
            where TImplementation : TService
        {
            return new ServiceDescriptor(
                typeof(TService),
                typeof(TImplementation),
                ServiceLifetime.Transient
            );
        }

        /// <summary>
        ///     Creates a transient service descriptor with a factory function.
        /// </summary>
        public static ServiceDescriptor Transient<TService>(Func<IServiceContainer, TService> factory)
        {
            return new ServiceDescriptor(
                typeof(TService),
                null,
                ServiceLifetime.Transient,
                container => factory(container)
            );
        }

        /// <summary>
        ///     Returns a string representation of this service descriptor.
        /// </summary>
        public override string ToString()
        {
            string impl = ImplementationType?.Name ?? (Factory != null ? "Factory" : "Instance");
            return $"{ServiceType.Name} -> {impl} ({Lifetime})";
        }
    }
}
