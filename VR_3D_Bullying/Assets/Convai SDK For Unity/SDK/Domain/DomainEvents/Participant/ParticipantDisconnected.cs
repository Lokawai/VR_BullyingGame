using System;

namespace Convai.Domain.DomainEvents.Participant
{
    /// <summary>
    ///     Domain event raised when a participant leaves the Convai session.
    ///     Replaces reflection-based RaiseParticipantLeft calls with strongly-typed EventHub publishing.
    /// </summary>
    /// <remarks>
    ///     This event is published via EventHub when a remote participant (character or player)
    ///     disconnects from the LiveKit room. Subscribe to this event instead of using the legacy
    ///     ConvaiRoomSession.ParticipantLeft event.
    ///     Usage:
    ///     <code>
    /// _eventHub.Subscribe&lt;ParticipantDisconnected&gt;(e =>
    /// {
    ///     Debug.Log($"Participant left: {e.Participant.DisplayName}");
    /// });
    /// </code>
    /// </remarks>
    public readonly struct ParticipantDisconnected
    {
        /// <summary>
        ///     Information about the participant that disconnected.
        /// </summary>
        public ParticipantInfo Participant { get; }

        /// <summary>
        ///     When the participant disconnected (UTC).
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        ///     Creates a new ParticipantDisconnected event.
        /// </summary>
        public ParticipantDisconnected(ParticipantInfo participant, DateTime timestamp)
        {
            Participant = participant;
            Timestamp = timestamp;
        }

        /// <summary>
        ///     Creates a ParticipantDisconnected event with the current UTC timestamp.
        /// </summary>
        public static ParticipantDisconnected Create(ParticipantInfo participant) => new(participant, DateTime.UtcNow);

        /// <summary>
        ///     Creates a ParticipantDisconnected event for a Convai character.
        /// </summary>
        public static ParticipantDisconnected ForCharacter(string participantId, string identity, string displayName) =>
            Create(ParticipantInfo.ForCharacter(participantId, identity, displayName));
    }
}
