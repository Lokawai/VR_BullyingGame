using Convai.Domain.Models;

namespace Convai.Application.Services
{
    /// <summary>
    ///     Interface for transcript service that handles character and player message broadcasting.
    ///     Publishes domain events via EventHub for decoupled communication.
    /// </summary>
    public interface IConvaiTranscriptService
    {
        /// <summary>
        ///     Broadcasts a character message to all subscribers via domain events.
        ///     Publishes CharacterTranscriptReceived event.
        /// </summary>
        public void BroadcastCharacterMessage(string charID, string charName, string message, bool isLastMessage);

        /// <summary>
        ///     Broadcasts a player message to all subscribers via domain events.
        ///     Publishes PlayerTranscriptReceived event.
        /// </summary>
        /// <param name="speakerID">The player's unique identifier</param>
        /// <param name="playerName">The player's display name</param>
        /// <param name="transcript">The transcript text</param>
        /// <param name="finalTranscript">Whether this is a final transcript</param>
        /// <param name="phase">The transcription phase (optional, derived from finality if not specified)</param>
        public void BroadcastPlayerMessage(
            string speakerID,
            string playerName,
            string transcript,
            bool finalTranscript,
            TranscriptionPhase? phase = null);
    }
}
