using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;

namespace Convai.Application.Services.Transcript
{
    /// <summary>
    ///     Export format for conversation history.
    /// </summary>
    public enum ConversationExportFormat
    {
        /// <summary>Simple text format with timestamps.</summary>
        PlainText,

        /// <summary>JSON array format.</summary>
        Json,

        /// <summary>Markdown format with speaker headers.</summary>
        Markdown
    }

    /// <summary>
    ///     Entry in the conversation history.
    /// </summary>
    public sealed class TranscriptEntry
    {
        /// <summary>
        ///     Creates a new transcript entry.
        /// </summary>
        public TranscriptEntry(TranscriptMessage message, SpeakerType speaker)
        {
            Id = Guid.NewGuid().ToString("N")[..8];
            Message = message;
            Speaker = speaker;
        }

        /// <summary>
        ///     Unique identifier for this entry.
        /// </summary>
        public string Id { get; }

        /// <summary>
        ///     The transcript message content.
        /// </summary>
        public TranscriptMessage Message { get; }

        /// <summary>
        ///     Speaker type (Character or Player).
        /// </summary>
        public SpeakerType Speaker { get; }
    }

    /// <summary>
    ///     Interface for conversation history management.
    /// </summary>
    internal interface IConversationHistory : IDisposable
    {
        /// <summary>
        ///     Gets all transcript entries in chronological order.
        /// </summary>
        public IReadOnlyList<TranscriptEntry> Entries { get; }

        /// <summary>
        ///     Gets the number of entries in the history.
        /// </summary>
        public int Count { get; }

        /// <summary>
        ///     Gets entries for a specific speaker.
        /// </summary>
        public IReadOnlyList<TranscriptEntry> GetEntriesBySpeaker(string speakerId);

        /// <summary>
        ///     Clears all history entries.
        /// </summary>
        public void Clear();

        /// <summary>
        ///     Exports history in the specified format.
        /// </summary>
        public string Export(ConversationExportFormat format);

        /// <summary>
        ///     Raised when a new entry is added.
        /// </summary>
        public event Action<TranscriptEntry> EntryAdded;
    }

    /// <summary>
    ///     In-memory implementation of conversation history.
    ///     Subscribes to transcript events and maintains history automatically.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This service automatically captures final transcripts from both characters and players,
    ///         storing them in chronological order. It provides export functionality for saving
    ///         conversation logs in various formats.
    ///     </para>
    ///     <para>
    ///         <b>Usage:</b>
    ///         <code>
    ///
    /// var history = new ConversationHistoryService(eventHub, maxEntries: 500);
    ///
    ///
    /// var entries = history.Entries;
    ///
    ///
    /// string Markdown = history.Export(ConversationExportFormat.Markdown);
    /// File.WriteAllText("conversation.md", Markdown);
    /// </code>
    ///     </para>
    /// </remarks>
    public sealed class ConversationHistoryService : IConversationHistory,
        IEventSubscriber<CharacterTranscriptReceived>,
        IEventSubscriber<PlayerTranscriptReceived>
    {
        private readonly SubscriptionToken _characterToken;
        private readonly List<TranscriptEntry> _entries = new();
        private readonly IEventHub _eventHub;
        private readonly object _lock = new();
        private readonly int _maxEntries;
        private readonly SubscriptionToken _playerToken;
        private bool _disposed;

        /// <summary>
        ///     Creates a new conversation history service.
        /// </summary>
        /// <param name="eventHub">Event hub for transcript subscriptions.</param>
        /// <param name="maxEntries">Maximum entries to keep (0 = unlimited).</param>
        public ConversationHistoryService(IEventHub eventHub, int maxEntries = 0)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _maxEntries = maxEntries;

            _characterToken = _eventHub.Subscribe<CharacterTranscriptReceived>(this);
            _playerToken = _eventHub.Subscribe<PlayerTranscriptReceived>(this);
        }

        /// <inheritdoc />
        public event Action<TranscriptEntry> EntryAdded = delegate { };

        /// <inheritdoc />
        public IReadOnlyList<TranscriptEntry> Entries
        {
            get
            {
                lock (_lock) return _entries.ToList().AsReadOnly();
            }
        }

        /// <inheritdoc />
        public int Count
        {
            get
            {
                lock (_lock) return _entries.Count;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<TranscriptEntry> GetEntriesBySpeaker(string speakerId)
        {
            lock (_lock)
            {
                return _entries
                    .Where(e => e.Message.SpeakerId == speakerId)
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            lock (_lock) _entries.Clear();
        }

        /// <inheritdoc />
        public string Export(ConversationExportFormat format)
        {
            List<TranscriptEntry> snapshot;
            lock (_lock) snapshot = _entries.ToList();

            return format switch
            {
                ConversationExportFormat.PlainText => ExportPlainText(snapshot),
                ConversationExportFormat.Json => ExportJson(snapshot),
                ConversationExportFormat.Markdown => ExportMarkdown(snapshot),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _eventHub.Unsubscribe(_characterToken);
            _eventHub.Unsubscribe(_playerToken);
        }

        /// <inheritdoc />
        public void OnEvent(CharacterTranscriptReceived e)
        {
            if (!e.Message.IsFinal) return;
            if (string.IsNullOrWhiteSpace(e.Message.Text)) return;

            AddEntry(new TranscriptEntry(e.Message, SpeakerType.Character));
        }

        /// <inheritdoc />
        public void OnEvent(PlayerTranscriptReceived e)
        {
            if (e.Phase != TranscriptionPhase.ProcessedFinal && e.Phase != TranscriptionPhase.Completed) return;
            if (string.IsNullOrWhiteSpace(e.Message.Text)) return;

            AddEntry(new TranscriptEntry(e.Message, SpeakerType.Player));
        }

        private void AddEntry(TranscriptEntry entry)
        {
            lock (_lock)
            {
                if (_maxEntries > 0 && _entries.Count >= _maxEntries) _entries.RemoveAt(0);
                _entries.Add(entry);
            }

            EntryAdded(entry);
        }

        private static string ExportPlainText(List<TranscriptEntry> entries)
        {
            StringBuilder sb = new();
            foreach (TranscriptEntry entry in entries)
            {
                sb.AppendLine(
                    $"[{entry.Message.Timestamp:HH:mm:ss}] {entry.Message.DisplayName}: {entry.Message.Text}");
            }

            return sb.ToString();
        }

        private static string ExportJson(List<TranscriptEntry> entries)
        {
            StringBuilder sb = new();
            sb.Append('[');
            for (int i = 0; i < entries.Count; i++)
            {
                TranscriptEntry e = entries[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append($"\"id\":\"{e.Id}\",");
                sb.Append($"\"speaker\":\"{EscapeJson(e.Message.DisplayName)}\",");
                sb.Append($"\"speakerId\":\"{EscapeJson(e.Message.SpeakerId)}\",");
                sb.Append($"\"speakerType\":\"{e.Speaker}\",");
                sb.Append($"\"text\":\"{EscapeJson(e.Message.Text)}\",");
                sb.Append($"\"timestamp\":\"{e.Message.Timestamp:O}\",");
                if (!string.IsNullOrEmpty(e.Message.ParticipantId))
                    sb.Append($"\"participantId\":\"{EscapeJson(e.Message.ParticipantId)}\",");
                sb.Append($"\"isFinal\":{(e.Message.IsFinal ? "true" : "false")}");
                sb.Append('}');
            }

            sb.Append(']');
            return sb.ToString();
        }

        private static string ExportMarkdown(List<TranscriptEntry> entries)
        {
            StringBuilder sb = new();
            sb.AppendLine("# Conversation Transcript");
            sb.AppendLine();
            sb.AppendLine($"*Exported at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (TranscriptEntry entry in entries)
            {
                string speakerType = entry.Speaker == SpeakerType.Character ? "AI" : "Player";
                sb.AppendLine($"**{entry.Message.DisplayName}** ({speakerType}) - {entry.Message.Timestamp:HH:mm:ss}");
                sb.AppendLine();
                sb.AppendLine($"> {entry.Message.Text}");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine($"*Total messages: {entries.Count}*");

            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
