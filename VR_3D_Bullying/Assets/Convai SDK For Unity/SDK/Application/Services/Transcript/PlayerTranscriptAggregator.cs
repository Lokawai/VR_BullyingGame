using System.Text;
using Convai.Domain.Models;

namespace Convai.Application.Services.Transcript
{
    /// <summary>
    ///     Default implementation of <see cref="IPlayerTranscriptAggregator" />.
    ///     Aggregates player transcript phases within a turn to produce a final transcript message.
    /// </summary>
    /// <remarks>
    ///     This class buffers AsrFinal text (with multi-sentence concatenation) and ProcessedFinal text,
    ///     then selects the best available text when the turn is completed.
    /// </remarks>
    public sealed class PlayerTranscriptAggregator : IPlayerTranscriptAggregator
    {
        private readonly StringBuilder _asrFinalBuilder = new();

        /// <inheritdoc />
        public string AsrFinalText => _asrFinalBuilder.ToString();

        /// <inheritdoc />
        public string ProcessedFinalText { get; private set; } = string.Empty;

        /// <inheritdoc />
        public SpeakerInfo SpeakerInfo { get; private set; } = SpeakerInfo.Empty;

        /// <inheritdoc />
        public string PlayerId { get; private set; }

        /// <inheritdoc />
        public string DisplayName { get; private set; }

        /// <inheritdoc />
        public string ParticipantId { get; private set; }

        /// <inheritdoc />
        public void AppendAsrFinal(string text, string speakerId = null, string displayName = null)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (!string.IsNullOrEmpty(speakerId)) PlayerId = speakerId;

            if (!string.IsNullOrEmpty(displayName)) DisplayName = displayName;

            // Concatenate with space separator for multi-sentence speech
            if (_asrFinalBuilder.Length > 0)
            {
                char lastChar = _asrFinalBuilder[^1];
                if (!char.IsWhiteSpace(lastChar)) _asrFinalBuilder.Append(' ');
            }

            _asrFinalBuilder.Append(text);
        }

        /// <inheritdoc />
        public void SetProcessedFinal(string text, string speakerId = null, string displayName = null,
            string participantId = null)
        {
            if (string.IsNullOrEmpty(text)) return;

            ProcessedFinalText = text;

            if (!string.IsNullOrEmpty(speakerId)) PlayerId = speakerId;

            if (!string.IsNullOrEmpty(displayName)) DisplayName = displayName;

            if (!string.IsNullOrEmpty(participantId)) ParticipantId = participantId;
        }

        /// <inheritdoc />
        public void UpdateSpeakerInfo(SpeakerInfo speakerInfo)
        {
            if (speakerInfo.IsValid) SpeakerInfo = speakerInfo;
        }

        /// <inheritdoc />
        public PlayerTranscriptResult Complete()
        {
            // Select best available text: ProcessedFinal > AsrFinal
            string finalText = !string.IsNullOrEmpty(ProcessedFinalText)
                ? ProcessedFinalText
                : AsrFinalText;

            PlayerTranscriptResult result = new(
                finalText,
                PlayerId,
                DisplayName,
                ParticipantId,
                SpeakerInfo);

            Reset();

            return result;
        }

        /// <inheritdoc />
        public void Reset()
        {
            PlayerId = null;
            DisplayName = null;
            ParticipantId = null;
            ProcessedFinalText = string.Empty;
            SpeakerInfo = SpeakerInfo.Empty;
            _asrFinalBuilder.Clear();
        }
    }
}
