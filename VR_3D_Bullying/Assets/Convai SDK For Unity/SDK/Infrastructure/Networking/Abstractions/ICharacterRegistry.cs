using System.Collections.Generic;
using Convai.Infrastructure.Networking.Models;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Registry for managing character descriptors.
    ///     Enables lookup by character ID or participant ID for multiplayer scenarios.
    /// </summary>
    public interface ICharacterRegistry
    {
        /// <summary>Registers a character with its descriptor.</summary>
        /// <param name="descriptor">The character descriptor to register.</param>
        public void RegisterCharacter(CharacterDescriptor descriptor);

        /// <summary>Removes a character from the registry.</summary>
        /// <param name="characterId">The character's unique identifier.</param>
        public void UnregisterCharacter(string characterId);

        /// <summary>Attempts to find a character by its ID.</summary>
        /// <param name="characterId">The character's unique identifier.</param>
        /// <param name="descriptor">The found descriptor, or default if not found.</param>
        /// <returns>True if the character was found; false otherwise.</returns>
        public bool TryGetCharacter(string characterId, out CharacterDescriptor descriptor);

        /// <summary>Attempts to find a character by its participant ID (from transport layer).</summary>
        /// <param name="participantId">The participant ID from the transport layer.</param>
        /// <param name="descriptor">The found descriptor, or default if not found.</param>
        /// <returns>True if the character was found; false otherwise.</returns>
        public bool TryGetCharacterByParticipantId(string participantId, out CharacterDescriptor descriptor);

        /// <summary>Gets all registered character descriptors.</summary>
        /// <returns>A read-only list of all registered characters.</returns>
        public IReadOnlyList<CharacterDescriptor> GetAllCharacters();

        /// <summary>Sets the muted state for a character.</summary>
        /// <param name="characterId">The character's unique identifier.</param>
        /// <param name="muted">True to mute; false to unmute.</param>
        public void SetCharacterMuted(string characterId, bool muted);

        /// <summary>Removes all registered characters.</summary>
        public void Clear();
    }
}
