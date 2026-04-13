using System;

namespace Convai.Runtime
{
    /// <summary>
    ///     Raised when platform networking composition cannot be completed during bootstrap.
    /// </summary>
    internal sealed class PlatformNetworkingBootstrapException : InvalidOperationException
    {
        internal PlatformNetworkingBootstrapException(string message)
            : base(message)
        {
        }
    }
}