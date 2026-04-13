using System;
using System.Collections.Generic;

namespace Convai.Infrastructure.Networking.Bootstrap
{
    /// <summary>
    ///     Process-wide registry populated by platform bootstraps before scene load.
    /// </summary>
    internal static class PlatformNetworkingRegistrarRegistry
    {
        private static readonly object Lock = new();
        private static readonly List<IPlatformNetworkingRegistrar> Registrars = new();

        internal static void Register(IPlatformNetworkingRegistrar registrar)
        {
            if (registrar == null) throw new ArgumentNullException(nameof(registrar));

            if (string.IsNullOrWhiteSpace(registrar.Id))
            {
                throw new ArgumentException("Platform networking registrar id cannot be null or whitespace.",
                    nameof(registrar));
            }

            lock (Lock)
            {
                for (int i = 0; i < Registrars.Count; i++)
                {
                    IPlatformNetworkingRegistrar existing = Registrars[i];
                    if (!string.Equals(existing.Id, registrar.Id, StringComparison.Ordinal)) continue;

                    if (existing.GetType() == registrar.GetType()) return;

                    throw new InvalidOperationException(
                        $"A platform networking registrar with id '{registrar.Id}' is already registered by '{existing.GetType().FullName}'.");
                }

                Registrars.Add(registrar);
            }
        }

        internal static IReadOnlyList<IPlatformNetworkingRegistrar> GetRegistrars()
        {
            lock (Lock)
            {
                return Registrars.ToArray();
            }
        }

        internal static void ResetForTests()
        {
            lock (Lock)
            {
                Registrars.Clear();
            }
        }
    }
}