using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking.Models;

namespace Convai.Infrastructure.Networking.Services
{
    /// <summary>
    ///     Implementation of ICharacterDiscoveryService for discovering and mapping characters to participants.
    /// </summary>
    /// <remarks>
    ///     Extracted from room controller to adhere to Single Responsibility Principle.
    ///     Manages:
    ///     - Identity-based matching (exact match to CharacterId)
    ///     - Fallback strategy (use first registered character)
    ///     - Bidirectional mapping between participants and characters
    ///     Thread-safe: Uses locking for concurrent access.
    /// </remarks>
    public sealed class CharacterDiscoveryService
    {
        private readonly ICharacterRegistry _characterRegistry;
        private readonly Dictionary<string, string> _characterToParticipant = new();
        private readonly ILogger _logger;
        private readonly object _mappingLock = new();

        private readonly Dictionary<string, string> _participantToCharacter = new();

        /// <summary>
        ///     Creates a new CharacterDiscoveryService.
        /// </summary>
        /// <param name="characterRegistry">Registry of available characters</param>
        /// <param name="logger">Logger for diagnostic messages (can be null)</param>
        public CharacterDiscoveryService(ICharacterRegistry characterRegistry, ILogger logger = null)
        {
            _characterRegistry = characterRegistry ?? throw new ArgumentNullException(nameof(characterRegistry));
            _logger = logger;
        }

        /// <inheritdoc />
        public event Action<CharacterDescriptor, string> CharacterMapped;

        /// <inheritdoc />
        public event Action<string, string> CharacterUnmapped;

        /// <inheritdoc />
        public void OnParticipantJoined(string participantSid, string participantIdentity, string displayName)
        {
            if (string.IsNullOrEmpty(participantSid))
            {
                _logger?.Warning("[CharacterDiscoveryService] OnParticipantJoined called with empty participantSid",
                    LogCategory.Character);
                return;
            }

            _logger?.Info(
                $"[CharacterDiscoveryService] Participant joined: {participantIdentity} (SID: {participantSid})",
                LogCategory.Character);

            CharacterDescriptor? matchedCharacter = FindCharacterForParticipant(participantIdentity);
            if (!matchedCharacter.HasValue)
            {
                _logger?.Debug(
                    $"[CharacterDiscoveryService] No character matched for participant: {participantIdentity}",
                    LogCategory.Character);
                return;
            }

            CharacterDescriptor descriptor = matchedCharacter.Value;
            string characterId = descriptor.CharacterId;

            CharacterDescriptor updated = descriptor.WithParticipantId(participantSid);
            _characterRegistry.RegisterCharacter(updated);

            lock (_mappingLock)
            {
                _participantToCharacter[participantSid] = characterId;
                _characterToParticipant[characterId] = participantSid;
            }

            _logger?.Info(
                $"[CharacterDiscoveryService] Mapped participant {participantIdentity} (SID: {participantSid}) to Character: {characterId}",
                LogCategory.Character);

            CharacterMapped?.Invoke(updated, participantSid);
        }

        /// <inheritdoc />
        public void OnParticipantLeft(string participantSid)
        {
            if (string.IsNullOrEmpty(participantSid)) return;

            _logger?.Info($"[CharacterDiscoveryService] Participant left: {participantSid}", LogCategory.Character);

            string characterId = null;

            lock (_mappingLock)
            {
                if (_participantToCharacter.TryGetValue(participantSid, out characterId))
                {
                    _participantToCharacter.Remove(participantSid);
                    _characterToParticipant.Remove(characterId);
                }
            }

            if (characterId != null)
            {
                if (_characterRegistry.TryGetCharacter(characterId, out CharacterDescriptor descriptor))
                {
                    _characterRegistry.RegisterCharacter(descriptor.WithParticipantId(string.Empty));
                    _logger?.Debug(
                        $"[CharacterDiscoveryService] Cleared participant mapping for Character: {characterId}",
                        LogCategory.Character);
                }

                CharacterUnmapped?.Invoke(characterId, participantSid);
            }
        }

        /// <inheritdoc />
        public CharacterDescriptor? FindCharacterForParticipant(string participantIdentity)
        {
            if (string.IsNullOrEmpty(participantIdentity)) return null;

            if (_characterRegistry.TryGetCharacter(participantIdentity, out CharacterDescriptor matched))
            {
                _logger?.Debug(
                    $"[CharacterDiscoveryService] Exact identity match found: {participantIdentity} -> {matched.CharacterId}",
                    LogCategory.Character);
                return matched;
            }

            IReadOnlyList<CharacterDescriptor> allCharacters = _characterRegistry.GetAllCharacters();
            if (allCharacters.Count == 0)
            {
                _logger?.Debug("[CharacterDiscoveryService] Cannot map participant: No characters in registry",
                    LogCategory.Character);
                return null;
            }

            CharacterDescriptor fallback = allCharacters[0];
            _logger?.Debug(
                $"[CharacterDiscoveryService] No exact match for '{participantIdentity}'. Using fallback Character: {fallback.CharacterId}",
                LogCategory.Character);
            return fallback;
        }

        /// <inheritdoc />
        public string GetCharacterIdForParticipant(string participantSid)
        {
            if (string.IsNullOrEmpty(participantSid)) return null;

            lock (_mappingLock)
                return _participantToCharacter.TryGetValue(participantSid, out string characterId) ? characterId : null;
        }

        /// <inheritdoc />
        public string GetParticipantForCharacter(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;

            lock (_mappingLock)
            {
                return _characterToParticipant.TryGetValue(characterId, out string participantSid)
                    ? participantSid
                    : null;
            }
        }
    }
}
