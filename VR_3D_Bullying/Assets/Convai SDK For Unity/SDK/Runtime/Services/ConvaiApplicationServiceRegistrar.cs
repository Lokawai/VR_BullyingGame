using System;
using Convai.Application.Services;
using Convai.Application.Services.Transcript;
using Convai.Application.Services.Vision;
using Convai.Domain.Abstractions;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Services;
using Convai.Runtime.Adapters.Platform;
using Convai.Runtime.Logging;
using Convai.Runtime.Services.CharacterLocator;
using Convai.Shared.Abstractions;
using Convai.Shared.DependencyInjection;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Services
{
    /// <summary>
    ///     Registers application-level services with the DI container.
    ///     Located in Unity layer since it depends on Unity-specific services.
    /// </summary>
    public static class ConvaiApplicationServiceRegistrar
    {
        /// <summary>
        ///     Registers all application services with the service locator.
        /// </summary>
        /// <param name="debugLogging">Whether to log registration messages.</param>
        public static void RegisterServices(bool debugLogging = false)
        {
            ConvaiServiceLocator.Register(
                ServiceDescriptor.Singleton<IConvaiTranscriptService>(container =>
                {
                    var eventHub = container.Get<IEventHub>();
                    container.TryGet(out ILogger logger);
                    return new ConvaiTranscriptService(eventHub, logger);
                }));

            if (debugLogging)
            {
                ConvaiLogger.Debug(
                    "[ConvaiApplicationServiceRegistrar] Registered IConvaiTranscriptService (ConvaiTranscriptService)",
                    LogCategory.Bootstrap);
            }

            ConvaiServiceLocator.Register(
                ServiceDescriptor.Singleton<ITranscriptFormatter, DefaultTranscriptFormatter>());

            ConvaiServiceLocator.Register(
                ServiceDescriptor.Singleton<ITranscriptFilter, DefaultTranscriptFilter>());

            if (debugLogging)
            {
                ConvaiLogger.Debug("[ConvaiApplicationServiceRegistrar] Registered transcript formatter/filter",
                    LogCategory.Bootstrap);
            }

            ConvaiServiceLocator.Register(
                ServiceDescriptor.Singleton<IConvaiCharacterLocatorService, ConvaiCharacterLocatorService>());

            if (debugLogging)
            {
                ConvaiLogger.Debug(
                    "[ConvaiApplicationServiceRegistrar] Registered IConvaiCharacterLocatorService and Unity alias",
                    LogCategory.Bootstrap);
            }

            ConvaiServiceLocator.Register(
                ServiceDescriptor.Singleton<IConvaiPermissionService, ConvaiPermissionService>());

            if (debugLogging)
            {
                ConvaiLogger.Debug("[ConvaiApplicationServiceRegistrar] Registered IConvaiPermissionService",
                    LogCategory.Bootstrap);
            }

            RegisterVisionServices(debugLogging);

            RegisterNarrativeServices(debugLogging);
        }

        /// <summary>
        ///     Registers Vision services with the DI container.
        /// </summary>
        /// <remarks>
        ///     Registers:
        ///     - IVisionService: Application-layer orchestration for vision capture and publishing
        ///     Note: IVideoTrackUnpublisher is created per-room via VideoTrackUnpublisherAdapter,
        ///     so VisionService receives it via SetVideoUnpublisher() when room connects.
        /// </remarks>
        private static void RegisterVisionServices(bool debugLogging)
        {
            try
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<IVisionService>(container =>
                    {
                        var eventHub = container.Get<IEventHub>();
                        container.TryGet(out ILogger logger);

                        return new VisionService(null, eventHub, logger);
                    }));

                if (debugLogging)
                {
                    ConvaiLogger.Debug("[ConvaiApplicationServiceRegistrar] Registered IVisionService (VisionService)",
                        LogCategory.Bootstrap);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error(
                    $"[ConvaiApplicationServiceRegistrar] Failed to register Vision services: {ex.Message}\n{ex.StackTrace}",
                    LogCategory.Bootstrap);
            }
        }

        private static void RegisterNarrativeServices(bool debugLogging)
        {
            try
            {
                ConvaiServiceLocator.Register(
                    ServiceDescriptor.Singleton<INarrativeDesignDataService>(container =>
                    {
                        Func<string> apiKeyProvider = () =>
                        {
                            if (ConvaiServiceLocator.TryGet(
                                    out IConvaiSettingsProvider settingsProvider)) return settingsProvider.ApiKey;
                            return ConvaiSettings.Instance?.ApiKey;
                        };

                        return new NarrativeDesignDataService(apiKeyProvider);
                    }));

                if (debugLogging)
                {
                    ConvaiLogger.Debug(
                        "[ConvaiApplicationServiceRegistrar] Registered INarrativeDesignDataService (NarrativeDesignDataService)",
                        LogCategory.Bootstrap);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error(
                    $"[ConvaiApplicationServiceRegistrar] Failed to register Narrative services: {ex.Message}\n{ex.StackTrace}",
                    LogCategory.Bootstrap);
            }
        }
    }
}
