using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Bootstrap;
using Convai.Infrastructure.Networking.Transport;
using Convai.Runtime.Logging;
using Convai.Shared.DependencyInjection;

namespace Convai.Runtime
{
    /// <summary>
    ///     Central runtime-owned selector that applies exactly one platform networking registrar.
    /// </summary>
    internal static class PlatformNetworkingBootstrap
    {
        private static readonly object Lock = new();
        private static bool _servicesRegistered;
        private static string _activeRegistrarId;

        internal static string ActiveRegistrarId => _activeRegistrarId;

        internal static void EnsureRegistered(bool debugLogging)
        {
            if (_servicesRegistered && AreRequiredPlatformServicesAvailable()) return;

            lock (Lock)
            {
                if (_servicesRegistered && AreRequiredPlatformServicesAvailable()) return;

                if (!ConvaiServiceLocator.IsInitialized) ConvaiServiceLocator.Initialize();

                IPlatformNetworkingRegistrar registrar = SelectRegistrar(debugLogging);
                registrar.RegisterServices();
                ValidateRequiredPlatformServices(registrar);

                _activeRegistrarId = registrar.Id;
                _servicesRegistered = true;

                if (debugLogging)
                {
                    ConvaiLogger.Debug(
                        $"[PlatformNetworkingBootstrap] Registered platform networking services via registrar '{registrar.Id}'.",
                        LogCategory.Bootstrap);
                }
            }
        }

        internal static void ResetForTests()
        {
            lock (Lock)
            {
                _servicesRegistered = false;
                _activeRegistrarId = null;
            }
        }

        private static IPlatformNetworkingRegistrar SelectRegistrar(bool debugLogging)
        {
            IReadOnlyList<IPlatformNetworkingRegistrar> registrars = PlatformNetworkingRegistrarRegistry.GetRegistrars();
            if (registrars.Count == 0)
            {
                throw new PlatformNetworkingBootstrapException(
                    "No platform networking registrar was discovered for the current runtime. " +
                    "This usually means the platform-specific networking assembly did not load or was stripped before its runtime bootstrap executed.");
            }

            IPlatformNetworkingRegistrar selected = null;
            int supportedCount = 0;

            for (int i = 0; i < registrars.Count; i++)
            {
                IPlatformNetworkingRegistrar registrar = registrars[i];
                if (!registrar.SupportsCurrentEnvironment()) continue;

                supportedCount++;

                if (selected == null || registrar.Priority > selected.Priority)
                {
                    selected = registrar;
                    continue;
                }

                if (registrar.Priority == selected.Priority &&
                    !string.Equals(registrar.Id, selected.Id, StringComparison.Ordinal))
                {
                    throw new PlatformNetworkingBootstrapException(
                        $"Multiple platform networking registrars support the current environment with the same priority ({registrar.Priority}): '{selected.Id}' and '{registrar.Id}'.");
                }
            }

            if (selected == null)
            {
                throw new PlatformNetworkingBootstrapException(
                    $"No platform networking registrar supports the current environment '{RealtimeTransportFactory.CurrentPlatform}'. " +
                    $"Discovered registrars: {DescribeRegistrars(registrars)}.");
            }

            if (debugLogging && supportedCount > 1)
            {
                ConvaiLogger.Debug(
                    $"[PlatformNetworkingBootstrap] Selected registrar '{selected.Id}' from {supportedCount} supported candidates.",
                    LogCategory.Bootstrap);
            }

            return selected;
        }

        private static void ValidateRequiredPlatformServices(IPlatformNetworkingRegistrar registrar)
        {
            if (!RealtimeTransportFactory.IsFactoryRegistered)
            {
                throw new PlatformNetworkingBootstrapException(
                    $"Platform registrar '{registrar.Id}' did not register {nameof(RealtimeTransportFactory)}.");
            }

            EnsureServiceRegistered<IConvaiRoomControllerFactory>(registrar);
            EnsureServiceRegistered<IMicrophoneSourceFactory>(registrar);
            EnsureServiceRegistered<IVideoSourceFactory>(registrar);
            EnsureServiceRegistered<IAudioStreamFactory>(registrar);
            EnsureServiceRegistered<IRealtimeTransportAccessor>(registrar);
        }

        private static void EnsureServiceRegistered<TService>(IPlatformNetworkingRegistrar registrar)
        {
            if (ConvaiServiceLocator.IsRegistered<TService>()) return;

            throw new PlatformNetworkingBootstrapException(
                $"Platform registrar '{registrar.Id}' did not register required service '{typeof(TService).Name}'.");
        }

        private static bool AreRequiredPlatformServicesAvailable()
        {
            if (!ConvaiServiceLocator.IsInitialized) return false;

            return RealtimeTransportFactory.IsFactoryRegistered
                   && ConvaiServiceLocator.IsRegistered<IConvaiRoomControllerFactory>()
                   && ConvaiServiceLocator.IsRegistered<IMicrophoneSourceFactory>()
                   && ConvaiServiceLocator.IsRegistered<IVideoSourceFactory>()
                   && ConvaiServiceLocator.IsRegistered<IAudioStreamFactory>()
                   && ConvaiServiceLocator.IsRegistered<IRealtimeTransportAccessor>();
        }

        private static string DescribeRegistrars(IReadOnlyList<IPlatformNetworkingRegistrar> registrars)
        {
            if (registrars == null || registrars.Count == 0) return "<none>";

            var parts = new string[registrars.Count];
            for (int i = 0; i < registrars.Count; i++)
            {
                IPlatformNetworkingRegistrar registrar = registrars[i];
                parts[i] = $"{registrar.Id} (priority {registrar.Priority})";
            }

            return string.Join(", ", parts);
        }
    }
}