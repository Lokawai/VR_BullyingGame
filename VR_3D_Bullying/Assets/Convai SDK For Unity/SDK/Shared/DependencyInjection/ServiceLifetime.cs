namespace Convai.Shared.DependencyInjection
{
    /// <summary>
    ///     Specifies the lifetime of a service in the dependency injection container.
    /// </summary>
    /// <remarks>
    ///     Service lifetimes determine how instances are created and shared:
    ///     - **Singleton**: One instance shared across the entire application lifetime.
    ///     Created once on first request and reused for all subsequent requests.
    ///     Thread-safe creation guaranteed.
    ///     - **Transient**: New instance created for each request.
    ///     No sharing between consumers.
    ///     Useful for lightweight, stateless services.
    ///     Usage Example:
    ///     <code>
    /// 
    /// container.Register&lt;ILogger, UnityLogger&gt;(ServiceLifetime.Singleton);
    /// 
    /// 
    /// container.Register&lt;ICommand, MyCommand&gt;(ServiceLifetime.Transient);
    /// </code>
    /// </remarks>
    public enum ServiceLifetime
    {
        /// <summary>
        ///     A single instance is created and shared for the entire application lifetime.
        ///     The instance is created on first request and reused for all subsequent requests.
        ///     Thread-safe creation is guaranteed.
        /// </summary>
        Singleton,

        /// <summary>
        ///     A new instance is created each time the service is requested.
        ///     No sharing occurs between consumers.
        /// </summary>
        Transient
    }
}
