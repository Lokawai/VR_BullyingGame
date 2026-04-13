using System;

namespace Convai.Domain.Logging
{
    /// <summary>
    ///     Represents a destination for log messages.
    ///     Implement this interface to add custom log outputs (file, remote, analytics, etc.).
    /// </summary>
    /// <remarks>
    ///     Log sinks receive formatted LogEntry structs and are responsible for:
    ///     - Formatting the entry for their specific output format
    ///     - Writing to their destination (console, file, network, etc.)
    ///     - Managing any buffering or batching
    ///     - Handling errors gracefully (should not throw)
    ///     Example implementations:
    ///     - UnityConsoleSink: Writes to Unity's Debug.Log
    ///     - FileSink: Writes to a log file on disk
    ///     - RemoteSink: Sends logs to a remote logging service
    ///     Thread-safety: Implementations should be thread-safe as Write() may be called
    ///     from multiple threads concurrently.
    ///     Performance optimizations:
    ///     - LogEntry passed by value (readonly struct is already optimized by JIT)
    ///     - Note: Using 'in' modifier is not recommended for readonly structs &lt;= 16 bytes
    ///     as it can introduce defensive copies in some scenarios
    /// </remarks>
    public interface ILogSink : IDisposable
    {
        /// <summary>
        ///     Gets the name of this sink for identification and debugging.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Gets whether this sink is currently enabled.
        ///     Disabled sinks will not receive Write() calls.
        /// </summary>
        public bool IsEnabled { get; }

        /// <summary>
        ///     Writes a log entry to this sink.
        /// </summary>
        /// <param name="entry">The log entry to write.</param>
        /// <remarks>
        ///     Implementations should:
        ///     - Handle null or empty messages gracefully
        ///     - Not throw exceptions (log errors internally if needed)
        ///     - Be thread-safe
        ///     - Complete quickly (buffer if I/O is slow)
        ///     Note: LogEntry is a readonly struct. For structs larger than 16 bytes,
        ///     passing by 'in' can reduce copying overhead. However, LogEntry contains
        ///     reference types, so the JIT will optimize appropriately.
        /// </remarks>
        public void Write(LogEntry entry);

        /// <summary>
        ///     Flushes any buffered log entries to the underlying destination.
        ///     Call this before application shutdown to ensure all logs are written.
        /// </summary>
        public void Flush();

        /// <summary>
        ///     Enables or disables this sink at runtime.
        /// </summary>
        /// <param name="enabled">True to enable, false to disable.</param>
        public void SetEnabled(bool enabled);
    }

    /// <summary>
    ///     Extension methods for ILogSink.
    /// </summary>
    public static class LogSinkExtensions
    {
        /// <summary>
        ///     Writes a log entry if the sink is enabled.
        ///     Uses 'in' parameter for performance when passing from optimized callers (P2-2).
        /// </summary>
        /// <param name="sink">The sink to write to.</param>
        /// <param name="entry">The log entry to write.</param>
        public static void WriteIfEnabled(this ILogSink sink, in LogEntry entry)
        {
            if (sink?.IsEnabled == true) sink.Write(entry);
        }
    }
}
