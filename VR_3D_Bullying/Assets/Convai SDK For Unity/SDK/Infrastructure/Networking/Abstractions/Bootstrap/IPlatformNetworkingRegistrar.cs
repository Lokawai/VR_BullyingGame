namespace Convai.Infrastructure.Networking.Bootstrap
{
    /// <summary>
    ///     Contract implemented by platform-specific networking modules so runtime bootstrap can
    ///     select and register exactly one active networking stack.
    /// </summary>
    internal interface IPlatformNetworkingRegistrar
    {
        /// <summary>
        ///     Stable identifier used for diagnostics and duplicate detection.
        /// </summary>
        string Id { get; }

        /// <summary>
        ///     Selection priority when multiple registrars support the current environment.
        ///     Higher values win.
        /// </summary>
        int Priority { get; }

        /// <summary>
        ///     Returns true when this registrar can service the current runtime environment.
        /// </summary>
        bool SupportsCurrentEnvironment();

        /// <summary>
        ///     Registers the platform-specific transport factory and DI services.
        /// </summary>
        void RegisterServices();
    }
}