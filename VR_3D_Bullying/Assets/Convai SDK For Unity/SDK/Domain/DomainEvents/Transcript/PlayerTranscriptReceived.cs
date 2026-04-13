using System;
using Convai.Domain.Models;

namespace Convai.Domain.DomainEvents.Transcript
{
    /// <summary>
    ///     Domain event raised when a player transcript is received.
    ///     Published via EventHub for decoupled transcript handling.
    ///     Supports multi-user speaker attribution via <see cref="SpeakerInfo" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This event is published via EventHub whenever a player generates a transcript
    ///         (any phase from speech recognition). The Application layer is responsible for
    ///         deciding which phases to display and how to aggregate them.
    ///     </para>
    ///     <para>
    ///         <b>Architecture Note:</b> This event carries the raw <see cref="TranscriptionPhase" />
    ///         from the Transport layer. The Application layer (TranscriptPresenter) should:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Suppress <c>Idle</c> and <c>Listening</c> phases (no display value)</description>
    ///             </item>
    ///             <item>
    ///                 <description>Display <c>Interim</c> phases as live transcription</description>
    ///             </item>
    ///             <item>
    ///                 <description>Buffer <c>AsrFinal</c> and <c>ProcessedFinal</c> for final text selection</description>
    ///             </item>
    ///             <item>
    ///                 <description>Emit final transcript on <c>Completed</c> using best available text</description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Multi-User Support:</b>
    ///         The <see cref="SpeakerInfo" /> property provides speaker attribution data from the backend's
    ///         speaker directory system. Use <see cref="HasSpeakerInfo" /> to check if multi-user
    ///         attribution is available for this transcript.
    ///     </para>
    /// </remarks>
    public readonly struct PlayerTranscriptReceived
    {
        /// <summary>
        ///     The transcript message from the player.
        /// </summary>
        public TranscriptMessage Message { get; }

        /// <summary>
        ///     The transcription phase this event represents.
        ///     Used by the Application layer to make decisions about filtering and aggregation.
        /// </summary>
        public TranscriptionPhase Phase { get; }

        /// <summary>
        ///     Speaker attribution data for multi-user scenarios.
        ///     Contains speaker_id, speaker_name, and participant_id from the backend.
        /// </summary>
        public SpeakerInfo SpeakerInfo { get; }

        /// <summary>
        ///     Creates a new PlayerTranscriptReceived event.
        /// </summary>
        public PlayerTranscriptReceived(
            TranscriptMessage message,
            TranscriptionPhase phase = TranscriptionPhase.Interim,
            SpeakerInfo speakerInfo = default)
        {
            Message = message;
            Phase = phase;
            SpeakerInfo = speakerInfo.IsValid ? speakerInfo : SpeakerInfo.Empty;
        }

        /// <summary>
        ///     Creates a PlayerTranscriptReceived event from individual parameters.
        /// </summary>
        /// <param name="playerId">The player's unique identifier</param>
        /// <param name="displayName">The player's display name</param>
        /// <param name="text">The transcript text</param>
        /// <param name="isFinal">Whether this is a final transcript</param>
        /// <param name="phase">The transcription phase</param>
        /// <param name="confidence">Optional confidence score from speech recognition</param>
        /// <param name="speakerInfo">Optional speaker attribution data</param>
        /// <returns>A new PlayerTranscriptReceived event</returns>
        public static PlayerTranscriptReceived Create(
            string playerId,
            string displayName,
            string text,
            bool isFinal,
            TranscriptionPhase phase = TranscriptionPhase.Interim,
            float? confidence = null,
            SpeakerInfo speakerInfo = default)
        {
            var message = TranscriptMessage.Create(
                playerId,
                displayName,
                text,
                isFinal,
                confidence,
                speakerInfo.ParticipantId,
                SpeakerType.Player
            );

            return new PlayerTranscriptReceived(message, phase, speakerInfo);
        }

        /// <summary>
        ///     Creates a PlayerTranscriptReceived event with speaker info.
        /// </summary>
        public static PlayerTranscriptReceived CreateWithSpeaker(
            string text,
            TranscriptionPhase phase,
            SpeakerInfo speakerInfo)
        {
            TranscriptMessage message = TranscriptMessage.ForPlayer(
                text,
                phase == TranscriptionPhase.ProcessedFinal || phase == TranscriptionPhase.Completed,
                speakerInfo.SpeakerId,
                speakerInfo.SpeakerName,
                speakerInfo.ParticipantId
            );

            return new PlayerTranscriptReceived(message, phase, speakerInfo);
        }

        /// <summary>
        ///     Gets the player ID from the message.
        /// </summary>
        public string PlayerId => Message.SpeakerId;

        /// <summary>
        ///     Gets the player's display name from the message.
        /// </summary>
        public string PlayerName => Message.DisplayName;

        /// <summary>
        ///     Gets the transcript text from the message.
        /// </summary>
        public string Text => Message.Text;

        /// <summary>
        ///     Checks if this is a final transcript.
        /// </summary>
        public bool IsFinal => Message.IsFinal;

        /// <summary>
        ///     Checks if this is an interim transcript.
        /// </summary>
        public bool IsInterim => Message.IsInterim;

        /// <summary>
        ///     Gets the timestamp when the transcript was received.
        /// </summary>
        public DateTime Timestamp => Message.Timestamp;

        /// <summary>
        ///     Gets the confidence score from speech recognition, if available.
        /// </summary>
        public float? Confidence => Message.Confidence;

        /// <summary>
        ///     Gets whether multi-user speaker attribution is available.
        /// </summary>
        public bool HasSpeakerInfo => SpeakerInfo.IsValid;

        /// <summary>
        ///     Gets the LiveKit participant ID, if available.
        /// </summary>
        public string ParticipantId => Message.ParticipantId;
    }
}
