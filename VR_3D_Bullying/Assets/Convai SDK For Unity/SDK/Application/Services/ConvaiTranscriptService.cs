using System;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Application.Services
{
    /// <summary>
    ///     Service for broadcasting transcript messages via domain events.
    ///     Publishes domain events via EventHub using constructor injection.
    /// </summary>
    public class ConvaiTranscriptService : IConvaiTranscriptService
    {
        private readonly IEventHub _eventHub;

        /// <summary>
        ///     Initializes a new instance of ConvaiTranscriptService with constructor injection.
        /// </summary>
        /// <param name="eventHub">Event hub for publishing domain events</param>
        /// <param name="logger">Logger for diagnostic messages (optional)</param>
        public ConvaiTranscriptService(IEventHub eventHub, ILogger logger = null)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
        }

        /// <summary>
        ///     Broadcasts a character message to all subscribers via domain events.
        /// </summary>
        public void BroadcastCharacterMessage(string charID, string charName, string message, bool isLastMessage)
        {
            var transcriptMessage = TranscriptMessage.Create(
                charID,
                charName,
                message,
                isLastMessage
            );

            _eventHub.Publish(new CharacterTranscriptReceived(transcriptMessage));
        }

        /// <summary>
        ///     Broadcasts a player message to all subscribers via domain events.
        /// </summary>
        /// <param name="speakerID">The player's unique identifier</param>
        /// <param name="playerName">The player's display name</param>
        /// <param name="transcript">The transcript text</param>
        /// <param name="finalTranscript">Whether this is a final transcript</param>
        /// <param name="phase">The transcription phase (defaults to Completed if final, Interim otherwise)</param>
        public void BroadcastPlayerMessage(
            string speakerID,
            string playerName,
            string transcript,
            bool finalTranscript,
            TranscriptionPhase? phase = null)
        {
            TranscriptionPhase actualPhase =
                phase ?? (finalTranscript ? TranscriptionPhase.Completed : TranscriptionPhase.Interim);

            var transcriptMessage = TranscriptMessage.Create(
                speakerID,
                playerName,
                transcript,
                finalTranscript
            );

            _eventHub.Publish(new PlayerTranscriptReceived(transcriptMessage, actualPhase));
        }
    }
}
