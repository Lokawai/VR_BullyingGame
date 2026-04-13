using System;
using Convai.Domain.Models;

namespace Convai.Runtime.Presentation.Presenters
{
    /// <summary>
    ///     Identifies the speaker associated with a transcript message.
    /// </summary>
    public enum TranscriptSpeaker
    {
        /// <summary>
        ///     The transcript is from an AI character.
        /// </summary>
        Character,

        /// <summary>
        ///     The transcript is from the player (user).
        /// </summary>
        Player
    }

    /// <summary>
    ///     View-model representation of a transcript message consumed by Unity UI layers.
    /// </summary>
    /// <remarks>
    ///     This struct provides a UI-friendly representation of transcript data, including:
    ///     - Speaker identification (Character or Player)
    ///     - The underlying domain message with all transcript details
    ///     - Pre-formatted text for display
    ///     TranscriptViewModel is immutable and thread-safe.
    /// </remarks>
    public readonly struct TranscriptViewModel
    {
        /// <summary>
        ///     Creates a new TranscriptViewModel.
        /// </summary>
        /// <param name="speaker">The speaker type (Character or Player).</param>
        /// <param name="message">The underlying transcript message.</param>
        /// <param name="formattedText">Pre-formatted text for display.</param>
        public TranscriptViewModel(TranscriptSpeaker speaker, TranscriptMessage message, string formattedText)
        {
            Speaker = speaker;
            Message = NormalizeMessage(message);
            FormattedText = formattedText ?? string.Empty;
        }

        /// <summary>
        ///     Gets the speaker type (Character or Player).
        /// </summary>
        public TranscriptSpeaker Speaker { get; }

        /// <summary>
        ///     Gets the underlying transcript message.
        /// </summary>
        public TranscriptMessage Message { get; }

        /// <summary>
        ///     Gets the pre-formatted text for display.
        /// </summary>
        public string FormattedText { get; }

        /// <summary>
        ///     Gets the unique identifier of the speaker.
        /// </summary>
        public string SpeakerId => Message.SpeakerId;

        /// <summary>
        ///     Gets the display name of the speaker.
        /// </summary>
        public string DisplayName => Message.DisplayName;

        /// <summary>
        ///     Gets the transcript text content.
        /// </summary>
        public string Text => Message.Text;

        /// <summary>
        ///     Gets whether this is the final transcript for the current utterance.
        /// </summary>
        public bool IsFinal => Message.IsFinal;

        /// <summary>
        ///     Gets the timestamp when the transcript was received.
        /// </summary>
        public DateTime Timestamp => Message.Timestamp;

        /// <summary>
        ///     Gets whether the transcript text is empty or whitespace.
        /// </summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

        /// <summary>
        ///     Gets the LiveKit participant ID for multi-user scenarios.
        /// </summary>
        public string ParticipantId => Message.ParticipantId;

        /// <summary>
        ///     Gets the speaker type (Character, Player, System, Unknown).
        /// </summary>
        public SpeakerType SpeakerType => Message.SpeakerType;

        /// <summary>
        ///     Gets whether this transcript has multi-user speaker attribution.
        /// </summary>
        public bool HasSpeakerInfo => !string.IsNullOrEmpty(SpeakerId) && SpeakerId != "local-player";

        private static TranscriptMessage NormalizeMessage(TranscriptMessage message)
        {
            string text = message.Text ?? string.Empty;
            string displayName = message.DisplayName ?? string.Empty;
            string speakerId = message.SpeakerId ?? string.Empty;
            string participantId = message.ParticipantId ?? string.Empty;
            DateTime timestamp = message.Timestamp == default ? DateTime.UtcNow : message.Timestamp;

            return new TranscriptMessage(
                speakerId,
                displayName,
                text,
                message.IsFinal,
                timestamp,
                message.Confidence,
                participantId,
                message.SpeakerType);
        }
    }
}
