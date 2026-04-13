namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Broadcasts transcript messages to the Convai SDK event system.
    ///     Bridges transport-layer transcripts to application-layer events.
    /// </summary>
    internal interface ITranscriptBroadcaster
    {
        /// <summary>Broadcasts a character's transcript message.</summary>
        /// <param name="characterId">The character's unique identifier.</param>
        /// <param name="characterName">The character's display name.</param>
        /// <param name="message">The transcript text content.</param>
        /// <param name="isFinal">True if this is the final version of the transcript.</param>
        public void BroadcastCharacterTranscript(string characterId, string characterName, string message,
            bool isFinal);

        /// <summary>Broadcasts a player's transcript message.</summary>
        /// <param name="playerId">The player's unique identifier.</param>
        /// <param name="playerName">The player's display name.</param>
        /// <param name="message">The transcript text content.</param>
        /// <param name="isFinal">True if this is the final version of the transcript.</param>
        public void BroadcastPlayerTranscript(string playerId, string playerName, string message, bool isFinal);

        /// <summary>Broadcasts an interaction ID for message correlation.</summary>
        /// <param name="characterId">The character's unique identifier.</param>
        /// <param name="interactionId">The interaction ID for correlating messages.</param>
        public void BroadcastInteractionId(string characterId, string interactionId);
    }
}
