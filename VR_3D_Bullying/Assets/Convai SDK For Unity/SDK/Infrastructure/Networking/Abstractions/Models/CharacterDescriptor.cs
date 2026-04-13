namespace Convai.Infrastructure.Networking.Models
{
    /// <summary>
    ///     Immutable descriptor describing a Convai Character routing metadata.
    ///     Audio source resolution is handled by Runtime layer via ICharacterAudioSourceResolver.
    /// </summary>
    public readonly struct CharacterDescriptor
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="CharacterDescriptor" /> struct.
        /// </summary>
        /// <param name="instanceId">Unity instance ID for the character GameObject.</param>
        /// <param name="characterId">Convai character identifier.</param>
        /// <param name="characterName">Human-readable display name.</param>
        /// <param name="participantId">Transport-layer participant identifier.</param>
        /// <param name="isMuted">Whether the character is muted.</param>
        public CharacterDescriptor(
            string instanceId,
            string characterId,
            string characterName,
            string participantId,
            bool isMuted)
        {
            InstanceId = instanceId ?? string.Empty;
            CharacterId = characterId ?? string.Empty;
            CharacterName = characterName ?? string.Empty;
            ParticipantId = participantId ?? string.Empty;
            IsMuted = isMuted;
        }

        /// <summary>
        ///     Unity instance ID for the character GameObject.
        /// </summary>
        public string InstanceId { get; }

        /// <summary>
        ///     Convai character ID.
        /// </summary>
        public string CharacterId { get; }

        /// <summary>
        ///     Human-readable display name for the character.
        /// </summary>
        public string CharacterName { get; }

        /// <summary>
        ///     Transport-layer participant ID (assigned when character joins room).
        /// </summary>
        public string ParticipantId { get; }

        /// <summary>
        ///     Whether this character's audio is muted.
        /// </summary>
        public bool IsMuted { get; }

        /// <summary>Creates a copy with the specified participant ID.</summary>
        /// <param name="participantId">The new participant ID.</param>
        /// <returns>A new descriptor with the updated participant ID.</returns>
        public CharacterDescriptor WithParticipantId(string participantId) =>
            new(InstanceId, CharacterId, CharacterName, participantId, IsMuted);

        /// <summary>Creates a copy with the specified mute state.</summary>
        /// <param name="isMuted">The new mute state.</param>
        /// <returns>A new descriptor with the updated mute state.</returns>
        public CharacterDescriptor WithMuteState(bool isMuted) =>
            new(InstanceId, CharacterId, CharacterName, ParticipantId, isMuted);
    }
}
