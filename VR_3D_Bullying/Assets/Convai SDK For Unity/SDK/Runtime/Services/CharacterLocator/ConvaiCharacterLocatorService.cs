using System.Collections.Generic;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Utilities;

namespace Convai.Runtime.Services.CharacterLocator
{
    /// <summary>
    ///     Locates IConvaiCharacterAgent and IConvaiPlayerAgent instances in the scene.
    /// </summary>
    /// <remarks>
    ///     Uses interface-based discovery to find all Character and player agents in the scene.
    /// </remarks>
    internal class ConvaiCharacterLocatorService : IConvaiCharacterLocatorService
    {
        private readonly List<IConvaiCharacterAgent> _characterAgents = new();
        private readonly List<IConvaiPlayerAgent> _playerAgents = new();

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConvaiCharacterLocatorService" /> class and performs an initial scene
        ///     scan.
        /// </summary>
        public ConvaiCharacterLocatorService()
        {
            IReadOnlyList<IConvaiCharacterAgent> characters =
                InterfaceComponentQuery.FindObjects<IConvaiCharacterAgent>();
            for (int i = 0; i < characters.Count; i++) _characterAgents.Add(characters[i]);

            IReadOnlyList<IConvaiPlayerAgent> players =
                InterfaceComponentQuery.FindObjects<IConvaiPlayerAgent>();
            for (int i = 0; i < players.Count; i++) _playerAgents.Add(players[i]);
        }

        /// <inheritdoc />
        public IReadOnlyList<IConvaiCharacterAgent> GetCharacterAgents() => _characterAgents;

        /// <inheritdoc />
        public IReadOnlyList<IConvaiPlayerAgent> GetPlayerAgents() => _playerAgents;

        /// <inheritdoc />
        public bool TryGetCharacter(string characterId, out IConvaiCharacterAgent agent)
        {
            agent = _characterAgents.Find(a => a.CharacterId == characterId);
            return agent != null;
        }

        /// <inheritdoc />
        public bool TryGetPlayer(string speakerId, out IConvaiPlayerAgent agent)
        {
            agent = _playerAgents.Find(p => p.SpeakerId == speakerId);
            return agent != null;
        }

        /// <inheritdoc />
        public void AddCharacter(IConvaiCharacterAgent character)
        {
            if (character != null && !_characterAgents.Contains(character)) _characterAgents.Add(character);
        }

        /// <inheritdoc />
        public void RemoveCharacter(IConvaiCharacterAgent character)
        {
            if (character != null) _characterAgents.Remove(character);
        }

        /// <inheritdoc />
        public void AddPlayer(IConvaiPlayerAgent player)
        {
            if (player != null && !_playerAgents.Contains(player)) _playerAgents.Add(player);
        }

        /// <inheritdoc />
        public void RemovePlayer(IConvaiPlayerAgent player)
        {
            if (player != null) _playerAgents.Remove(player);
        }
    }
}
