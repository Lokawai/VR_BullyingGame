using Convai.Shared.DependencyInjection;

namespace Convai.Shared
{
    /// <summary>
    ///     Contract for MonoBehaviour components that require dependency injection.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The <see cref="IInjectable" /> interface replaces the per-type <c>IInjectableXxx</c> interfaces
    ///         with a single generic contract. The composition root discovers all <see cref="IInjectable" />
    ///         components in the scene and calls <see cref="InjectServices" /> once during initialization.
    ///     </para>
    ///     <para>
    ///         Components resolve their own dependencies from the container, making dependencies
    ///         explicit and self-documenting:
    ///     </para>
    ///     <code>
    /// public class MyComponent : MonoBehaviour, IInjectable
    /// {
    ///     private IEventHub _eventHub;
    ///     private ILogger _logger;
    /// 
    ///     public void InjectServices(IServiceContainer container)
    ///     {
    ///         _eventHub = container.Get&lt;IEventHub&gt;();
    ///         container.TryGet(out _logger);
    ///     }
    /// }
    /// </code>
    ///     <para>
    ///         Components that also <b>register</b> themselves as services (e.g., ConvaiRoomManager registers
    ///         as IConvaiRoomConnectionService) should use a negative <see cref="InjectionOrder" /> so they
    ///         run before components that depend on those services.
    ///     </para>
    /// </remarks>
    public interface IInjectable
    {
        /// <summary>
        ///     Controls injection ordering. Lower values inject first.
        ///     Default is 0. Use negative values for infrastructure components that
        ///     register services needed by other injectables (e.g., ConvaiRoomManager = -100).
        /// </summary>
        public int InjectionOrder => 0;

        /// <summary>
        ///     Called by the composition root to inject dependencies.
        ///     Resolve your dependencies from the container. Components that register
        ///     themselves as services should also do so here.
        /// </summary>
        /// <param name="container">The service container to resolve dependencies from.</param>
        public void InjectServices(IServiceContainer container);
    }
}
