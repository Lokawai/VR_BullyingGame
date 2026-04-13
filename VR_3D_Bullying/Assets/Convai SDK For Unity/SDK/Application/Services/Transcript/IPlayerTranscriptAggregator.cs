using Convai.Domain.Models;

namespace Convai.Application.Services.Transcript
{
    /// <summary>
    ///     Aggregates player transcript phases within a turn to produce a final transcript message.
    ///     Handles buffering of AsrFinal/ProcessedFinal text and multi-sentence concatenation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Phase Processing:</b>
    ///         <list type="bullet">
    ///             <item>
    ///                 <description><c>Idle</c>, <c>Listening</c>: Suppressed (no display value)</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>Interim</c>: Displayed as live transcription (IsFinal=false)</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>AsrFinal</c>: Buffered and concatenated for final text selection</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>ProcessedFinal</c>: Buffered for final text selection (preferred over AsrFinal)</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>Completed</c>: Emits final transcript using the best available text</description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Final Text Selection:</b>
    ///         Preference: ProcessedFinal > AsrFinal (concatenated)
    ///     </para>
    /// </remarks>
    public interface IPlayerTranscriptAggregator
    {
        /// <summary>
        ///     Gets the current aggregated ASR final text (concatenated from multiple AsrFinal events).
        /// </summary>
        public string AsrFinalText { get; }

        /// <summary>
        ///     Gets the current processed final text (preferred over AsrFinal when available).
        /// </summary>
        public string ProcessedFinalText { get; }

        /// <summary>
        ///     Gets the current speaker info for the turn.
        /// </summary>
        public SpeakerInfo SpeakerInfo { get; }

        /// <summary>
        ///     Gets the player ID for the current turn.
        /// </summary>
        public string PlayerId { get; }

        /// <summary>
        ///     Gets the display name for the current turn.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        ///     Gets the participant ID for the current turn.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     Processes an ASR final event by appending text to the buffer.
        ///     Multiple ASR finals within a turn are concatenated (for pause-delimited speech).
        /// </summary>
        /// <param name="text">The ASR final text to append.</param>
        /// <param name="speakerId">Optional speaker ID.</param>
        /// <param name="displayName">Optional display name.</param>
        public void AppendAsrFinal(string text, string speakerId = null, string displayName = null);

        /// <summary>
        ///     Processes a processed final event by storing the text.
        /// </summary>
        /// <param name="text">The processed final text.</param>
        /// <param name="speakerId">Optional speaker ID.</param>
        /// <param name="displayName">Optional display name.</param>
        /// <param name="participantId">Optional participant ID.</param>
        public void SetProcessedFinal(string text, string speakerId = null, string displayName = null,
            string participantId = null);

        /// <summary>
        ///     Updates speaker info if the provided info is valid.
        /// </summary>
        /// <param name="speakerInfo">The speaker info to update.</param>
        public void UpdateSpeakerInfo(SpeakerInfo speakerInfo);

        /// <summary>
        ///     Completes the current turn and returns the best available final text.
        ///     Preference: ProcessedFinal > AsrFinal (concatenated).
        ///     Resets the aggregator state after completion.
        /// </summary>
        /// <returns>A result containing the final text and speaker metadata, or null if no text available.</returns>
        public PlayerTranscriptResult Complete();

        /// <summary>
        ///     Resets all aggregator state without producing a result.
        /// </summary>
        public void Reset();
    }

    /// <summary>
    ///     Result of completing a player transcript turn.
    /// </summary>
    public readonly struct PlayerTranscriptResult
    {
        /// <summary>
        ///     The final text selected from ProcessedFinal or AsrFinal.
        /// </summary>
        public string FinalText { get; }

        /// <summary>
        ///     The player/speaker ID.
        /// </summary>
        public string PlayerId { get; }

        /// <summary>
        ///     The display name for the speaker.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        ///     The participant ID.
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     The speaker info for multi-user attribution.
        /// </summary>
        public SpeakerInfo SpeakerInfo { get; }

        /// <summary>
        ///     Gets whether this result has valid final text.
        /// </summary>
        public bool HasText => !string.IsNullOrWhiteSpace(FinalText);

        /// <summary>
        ///     Initializes a new instance of the <see cref="PlayerTranscriptResult" /> struct.
        /// </summary>
        public PlayerTranscriptResult(
            string finalText,
            string playerId,
            string displayName,
            string participantId,
            SpeakerInfo speakerInfo)
        {
            FinalText = finalText;
            PlayerId = playerId;
            DisplayName = displayName;
            ParticipantId = participantId;
            SpeakerInfo = speakerInfo;
        }
    }
}
