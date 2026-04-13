using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Convai.Shared.DependencyInjection
{
    /// <summary>
    ///     Lightweight dependency injection container for managing service lifetimes and dependencies.
    /// </summary>
    /// <remarks>
    ///     Features:
    ///     - Service registration (Singleton, Transient)
    ///     - Constructor injection with automatic dependency resolution
    ///     - Thread-safe registration and singleton creation
    ///     - Lifecycle management (Initialize, Dispose)
    ///     - Circular dependency detection (per-thread)
    ///     Limitations:
    ///     - Constructor injection only (no property/method injection)
    ///     Thread Safety:
    ///     - Registration IS thread-safe (uses ConcurrentDictionary with TryAdd)
    ///     - Resolution IS thread-safe (uses ConcurrentDictionary and lock for singleton creation)
    ///     - Circular dependency detection is per-thread (ThreadLocal stack)
    /// </remarks>
    public class ServiceContainer : IServiceContainer
    {
        private readonly ConcurrentDictionary<Type, ConstructorMetadata> _constructorCache = new();
        private readonly ConcurrentDictionary<Type, ServiceDescriptor> _descriptors = new();
        private readonly ThreadLocal<Stack<Type>> _resolutionStack = new(() => new Stack<Type>());
        private readonly ConcurrentDictionary<Type, object> _singletonInstances = new();
        private readonly object _singletonLock = new();
        private bool _isDisposed;
        private bool _isInitialized;

        /// <summary>
        ///     Creates a new service container.
        /// </summary>
        public ServiceContainer()
        {
        }

        /// <inheritdoc />
        public void Register(ServiceDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));

            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ServiceContainer),
                    "Cannot register services on a disposed container");
            }

            if (descriptor.Instance != null)
            {
                if (descriptor.Lifetime != ServiceLifetime.Singleton)
                {
                    throw new InvalidOperationException(
                        $"Instance registration is only allowed for Singleton lifetime (Service: {descriptor.ServiceType.Name})");
                }
            }

            if (!_descriptors.TryAdd(descriptor.ServiceType, descriptor))
                throw new InvalidOperationException($"Service {descriptor.ServiceType.Name} is already registered");

            if (descriptor.Instance != null) _singletonInstances[descriptor.ServiceType] = descriptor.Instance;
        }

        /// <inheritdoc />
        public TService Get<TService>()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ServiceContainer),
                    "Cannot resolve services from a disposed container");
            }

            Type serviceType = typeof(TService);

            if (_descriptors.TryGetValue(serviceType, out ServiceDescriptor descriptor))
                return (TService)Resolve(descriptor);

            throw new InvalidOperationException($"Service {serviceType.Name} is not registered");
        }

        /// <inheritdoc />
        public bool TryGet<TService>(out TService service)
        {
            service = default;

            if (_isDisposed)
                return false;

            Type serviceType = typeof(TService);

            if (_descriptors.TryGetValue(serviceType, out ServiceDescriptor descriptor))
            {
                try
                {
                    service = (TService)Resolve(descriptor);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ServiceContainer] TryGet<{typeof(TService).Name}> failed: {ex.Message}");
                    service = default;
                    return false;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public bool IsRegistered<TService>() => _descriptors.ContainsKey(typeof(TService));

        /// <inheritdoc />
        public void Initialize()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ServiceContainer), "Cannot initialize a disposed container");

            if (_isInitialized) return;

            List<ServiceDescriptor> singletonDescriptors = _descriptors.Values
                .Where(d => d.Lifetime == ServiceLifetime.Singleton)
                .ToList();

            foreach (ServiceDescriptor descriptor in singletonDescriptors)
            {
                try
                {
                    Resolve(descriptor);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to initialize singleton service {descriptor.ServiceType.Name}. " +
                        "Check constructor dependencies and registration order. " +
                        $"Error: {ex.Message}",
                        ex
                    );
                }
            }

            _isInitialized = true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_isDisposed)
                return;

            foreach (object instance in _singletonInstances.Values)
            {
                if (instance is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[ServiceContainer] Dispose failed for {instance.GetType().Name}: {ex.Message}");
                    }
                }
            }

            _singletonInstances.Clear();
            _descriptors.Clear();
            _resolutionStack.Dispose();

            _isDisposed = true;
        }

        private object Resolve(ServiceDescriptor descriptor)
        {
            switch (descriptor.Lifetime)
            {
                case ServiceLifetime.Singleton:
                    return ResolveSingleton(descriptor);

                case ServiceLifetime.Transient:
                    return CreateInstance(descriptor);

                default:
                    throw new InvalidOperationException($"Unknown service lifetime: {descriptor.Lifetime}");
            }
        }

        private object ResolveSingleton(ServiceDescriptor descriptor)
        {
            return _singletonInstances.GetOrAdd(descriptor.ServiceType, _ =>
            {
                lock (_singletonLock)
                {
                    if (_singletonInstances.TryGetValue(descriptor.ServiceType, out object existing))
                        return existing;

                    return CreateInstance(descriptor);
                }
            });
        }

        private object CreateInstance(ServiceDescriptor descriptor)
        {
            if (descriptor.Factory != null)
                return descriptor.Factory(this);

            if (descriptor.Instance != null)
                return descriptor.Instance;

            if (descriptor.ImplementationType == null)
            {
                throw new InvalidOperationException(
                    $"Cannot create instance of {descriptor.ServiceType.Name}: no implementation type, factory, or instance provided");
            }

            return CreateInstanceWithConstructorInjection(descriptor.ImplementationType, descriptor.ServiceType);
        }

        private object CreateInstanceWithConstructorInjection(Type implementationType, Type serviceType)
        {
            Stack<Type> stack = _resolutionStack.Value;

            if (stack.Contains(serviceType))
            {
                string chain = string.Join(" -> ", stack.Select(t => t.Name));
                throw new InvalidOperationException($"Circular dependency detected: {chain} -> {serviceType.Name}");
            }

            stack.Push(serviceType);

            try
            {
                ConstructorMetadata metadata = GetOrCreateConstructorMetadata(implementationType);

                object[] resolvedParameters = new object[metadata.ParameterTypes.Length];
                for (int i = 0; i < metadata.ParameterTypes.Length; i++)
                {
                    Type paramType = metadata.ParameterTypes[i];
                    try
                    {
                        if (_descriptors.TryGetValue(paramType, out ServiceDescriptor paramDescriptor))
                            resolvedParameters[i] = Resolve(paramDescriptor);
                        else
                            throw new InvalidOperationException($"Service '{paramType.Name}' is not registered");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Failed to resolve dependency '{paramType.Name}' for service '{serviceType.Name}'. " +
                            $"Error: {ex.Message}",
                            ex
                        );
                    }
                }

                return metadata.Constructor.Invoke(resolvedParameters);
            }
            finally
            {
                _resolutionStack.Value.Pop();
            }
        }

        private ConstructorMetadata GetOrCreateConstructorMetadata(Type implementationType)
        {
            return _constructorCache.GetOrAdd(implementationType, type =>
            {
                ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

                if (constructors.Length == 0)
                    throw new InvalidOperationException($"No public constructors found for {type.Name}");

                ConstructorInfo bestConstructor = null;
                Type[] bestParameterTypes = null;
                int bestParameterCount = -1;

                foreach (ConstructorInfo constructor in constructors)
                {
                    ParameterInfo[] parameters = constructor.GetParameters();
                    var parameterTypes = new Type[parameters.Length];
                    bool canResolve = true;

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        parameterTypes[i] = parameters[i].ParameterType;

                        if (!_descriptors.ContainsKey(parameterTypes[i]))
                        {
                            canResolve = false;
                            break;
                        }
                    }

                    if (canResolve && parameters.Length > bestParameterCount)
                    {
                        bestConstructor = constructor;
                        bestParameterTypes = parameterTypes;
                        bestParameterCount = parameters.Length;
                    }
                }

                if (bestConstructor == null)
                {
                    throw new InvalidOperationException(
                        $"Cannot resolve constructor for {type.Name}. " +
                        "Ensure all constructor parameters are registered. " +
                        $"Available constructors: {string.Join(", ", constructors.Select(c => $"({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name))})"))}");
                }

                return new ConstructorMetadata(bestConstructor, bestParameterTypes);
            });
        }

        /// <summary>
        ///     Cached constructor metadata to avoid repeated reflection calls.
        /// </summary>
        private sealed class ConstructorMetadata
        {
            public ConstructorMetadata(ConstructorInfo constructor, Type[] parameterTypes)
            {
                Constructor = constructor;
                ParameterTypes = parameterTypes;
            }

            public ConstructorInfo Constructor { get; }
            public Type[] ParameterTypes { get; }
        }
    }
}
