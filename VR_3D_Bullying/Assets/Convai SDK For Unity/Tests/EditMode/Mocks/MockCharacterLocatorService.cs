using System.Collections.Generic;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Services.CharacterLocator;

namespace Convai.Tests.EditMode.Mocks
{
    /// <summary>
    ///     Lightweight mock for IConvaiCharacterLocatorService used in edit-mode tests.
    /// </summary>
    public sealed class MockCharacterLocatorService : IConvaiCharacterLocatorService
    {
        private readonly List<IConvaiCharacterAgent> _characterAgents = new();
        private readonly List<IConvaiPlayerAgent> _playerAgents = new();

        public IReadOnlyList<IConvaiCharacterAgent> GetCharacterAgents() => _characterAgents;

        public IReadOnlyList<IConvaiPlayerAgent> GetPlayerAgents() => _playerAgents;

        public bool TryGetCharacter(string characterId, out IConvaiCharacterAgent agent)
        {
            agent = _characterAgents.Find(a => a.CharacterId == characterId);
            return agent != null;
        }

        public bool TryGetPlayer(string speakerId, out IConvaiPlayerAgent agent)
        {
            agent = _playerAgents.Find(p => p.SpeakerId == speakerId);
            return agent != null;
        }

        public void AddCharacter(IConvaiCharacterAgent character)
        {
            if (character != null && !_characterAgents.Contains(character)) _characterAgents.Add(character);
        }

        public void RemoveCharacter(IConvaiCharacterAgent character)
        {
            if (character != null) _characterAgents.Remove(character);
        }

        public void AddPlayer(IConvaiPlayerAgent player)
        {
            if (player != null && !_playerAgents.Contains(player)) _playerAgents.Add(player);
        }

        public void RemovePlayer(IConvaiPlayerAgent player)
        {
            if (player != null) _playerAgents.Remove(player);
        }

        /// <summary>
        ///     Test helper to check if a character with the given ID is registered.
        /// </summary>
        public bool HasCharacter(string characterId) => TryGetCharacter(characterId, out _);
    }
}
