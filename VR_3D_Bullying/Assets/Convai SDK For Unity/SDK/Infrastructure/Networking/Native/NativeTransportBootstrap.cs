#if !UNITY_WEBGL || UNITY_EDITOR

using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Bootstrap;
using Convai.Runtime.Logging;
using UnityEngine;
using UnityEngine.Scripting;
// Type alias to disambiguate from UnityEngine.ILogger
using IConvaiLogger = Convai.Domain.Logging.ILogger;

namespace Convai.Infrastructure.Networking.Native
{
    /// <summary>
    ///     Thin runtime entrypoint that announces the native networking registrar before scene load.
    /// </summary>
    [Preserve]
    internal static class NativeTransportBootstrap
    {
        /// <summary>
        ///     Registers the native registrar before any scene loads.
        /// </summary>
        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            PlatformNetworkingRegistrarRegistry.Register(new NativeNetworkingRegistrar());
        }
    }

    /// <summary>
    ///     Fallback logger implementation that prefixes messages and forwards them to ConvaiLogger.
    ///     Used for transport logging when the native bootstrap path still needs an ILogger instance.
    /// </summary>
    internal sealed class NativeTransportFallbackLogger : IConvaiLogger
    {
        private const string Prefix = "[NativeTransport]";

        private static IConvaiLogger Logger => ConvaiLogger.Instance;

        public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) =>
            Logger.Log(level, PrefixMessage(message), category);

        public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) => Logger.Log(level, PrefixMessage(message), context, category);

        void IConvaiLogger.Debug(string message, LogCategory category) =>
            Logger.Debug(PrefixMessage(message), category);

        void IConvaiLogger.Debug(string message, IReadOnlyDictionary<string, object> context, LogCategory category) =>
            Logger.Debug(PrefixMessage(message), context, category);

        public void Info(string message, LogCategory category = LogCategory.SDK) =>
            Logger.Info(PrefixMessage(message), category);

        public void Info(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) => Logger.Info(PrefixMessage(message), context, category);

        public void Warning(string message, LogCategory category = LogCategory.SDK) =>
            Logger.Warning(PrefixMessage(message), category);

        public void Warning(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) => Logger.Warning(PrefixMessage(message), context, category);

        public void Error(string message, LogCategory category = LogCategory.SDK) =>
            Logger.Error(PrefixMessage(message), category);

        public void Error(string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) => Logger.Error(PrefixMessage(message), context, category);

        public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK) =>
            Logger.Error(exception, PrefixMessage(message), category);

        public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
            LogCategory category = LogCategory.SDK) =>
            Logger.Error(exception, PrefixMessage(message), context, category);

        public bool IsEnabled(LogLevel level, LogCategory category) => Logger.IsEnabled(level, category);

        private static string PrefixMessage(string message)
        {
            return string.IsNullOrWhiteSpace(message)
                ? Prefix
                : $"{Prefix} {message}";
        }
    }
}
#endif
