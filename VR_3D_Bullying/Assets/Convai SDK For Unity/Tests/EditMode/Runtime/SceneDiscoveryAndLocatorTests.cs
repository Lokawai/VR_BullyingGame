using System;
using System.Collections.Generic;
using System.Linq;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Runtime.Services.CharacterLocator;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Runtime
{
    public class SceneDiscoveryAndLocatorTests
    {
        private readonly List<GameObject> _createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _createdObjects.Count; i++)
            {
                GameObject go = _createdObjects[i];
                if (go != null) Object.DestroyImmediate(go);
            }

            _createdObjects.Clear();
        }

        [Test]
        public void SceneCharacterDiscovery_Discovers_BuiltIn_Player()
        {
            var playerGo = new GameObject("BuiltInPlayer");
            _createdObjects.Add(playerGo);
            var player = playerGo.AddComponent<ConvaiPlayer>();
            player.Configure("BuiltInPlayer", "built-in-speaker");

            var discovery = new SceneCharacterDiscovery();
            IConvaiPlayerAgent discovered = discovery.DiscoverPlayer();

            Assert.NotNull(discovered);
            Assert.AreSame(player, discovered);
        }

        [Test]
        public void SceneCharacterDiscovery_Discovers_Custom_PlayerAgent()
        {
            var playerGo = new GameObject("CustomPlayer");
            _createdObjects.Add(playerGo);
            var customPlayer = playerGo.AddComponent<CustomPlayerAgent>();
            customPlayer.Configure("CustomPlayer", "custom-speaker");

            var discovery = new SceneCharacterDiscovery();
            IConvaiPlayerAgent discovered = discovery.DiscoverPlayer();

            Assert.NotNull(discovered);
            Assert.AreSame(customPlayer, discovered);
            Assert.AreEqual("custom-speaker", discovered.SpeakerId);
        }

        [Test]
        public void SceneCharacterDiscovery_Discovers_BuiltIn_And_Custom_Characters()
        {
            var builtInCharacterGo = new GameObject("BuiltInCharacter");
            _createdObjects.Add(builtInCharacterGo);
            var builtInCharacter = builtInCharacterGo.AddComponent<ConvaiCharacter>();
            builtInCharacter.Configure("built-in-character-id", "Built In");

            var customCharacterGo = new GameObject("CustomCharacter");
            _createdObjects.Add(customCharacterGo);
            var customCharacter = customCharacterGo.AddComponent<CustomCharacterAgent>();
            customCharacter.Configure("custom-character-id", "Custom Character");

            var registry = new TestCharacterRegistry();
            var discovery = new SceneCharacterDiscovery();

            List<IConvaiCharacterAgent> discoveredCharacters = discovery.DiscoverCharacters(registry);

            Assert.That(discoveredCharacters, Has.Member(builtInCharacter));
            Assert.That(discoveredCharacters, Has.Member(customCharacter));

            Assert.IsTrue(registry.TryGetCharacter("built-in-character-id", out CharacterDescriptor builtInDescriptor));
            Assert.AreEqual("Built In", builtInDescriptor.CharacterName);

            Assert.IsTrue(registry.TryGetCharacter("custom-character-id", out CharacterDescriptor customDescriptor));
            Assert.AreEqual("Custom Character", customDescriptor.CharacterName);

            Assert.IsTrue(registry.TryGetAudioSource("built-in-character-id", out AudioSource builtInAudio));
            Assert.NotNull(builtInAudio);
            Assert.IsTrue(registry.TryGetAudioSource("custom-character-id", out AudioSource customAudio));
            Assert.NotNull(customAudio);
        }

        [Test]
        public void SceneCharacterDiscovery_WithBootstrapRegistry_ResolvesDiscoveredCharacterAudioSource()
        {
            var characterGo = new GameObject("BuiltInCharacter");
            _createdObjects.Add(characterGo);
            var character = characterGo.AddComponent<ConvaiCharacter>();
            character.Configure("built-in-character-id", "Built In");

            var discovery = new SceneCharacterDiscovery();
            var registry = new CharacterRegistryAdapter(discovery.CharacterToParticipantMap,
                new List<IConvaiCharacterAgent>());

            List<IConvaiCharacterAgent> discoveredCharacters = discovery.DiscoverCharacters(registry);

            Assert.That(discoveredCharacters, Has.Member(character));
            Assert.IsTrue(registry.TryGetAudioSource("built-in-character-id", out AudioSource audioSource),
                "Bootstrap registry should resolve the discovered character's AudioSource without being rebuilt.");
            Assert.NotNull(audioSource);

            registry.SetCharacterMuted("built-in-character-id", true);

            Assert.IsTrue(audioSource.mute,
                "Bootstrap registry should keep AudioSource routing and mute state aligned for discovered characters.");
        }

        [Test]
        public void CharacterLocatorService_Includes_BuiltIn_And_Custom_Agents()
        {
            var builtInCharacterGo = new GameObject("BuiltInCharacter");
            _createdObjects.Add(builtInCharacterGo);
            var builtInCharacter = builtInCharacterGo.AddComponent<ConvaiCharacter>();
            builtInCharacter.Configure("built-in-character-id", "Built In");

            var customCharacterGo = new GameObject("CustomCharacter");
            _createdObjects.Add(customCharacterGo);
            var customCharacter = customCharacterGo.AddComponent<CustomCharacterAgent>();
            customCharacter.Configure("custom-character-id", "Custom Character");

            var builtInPlayerGo = new GameObject("BuiltInPlayer");
            _createdObjects.Add(builtInPlayerGo);
            var builtInPlayer = builtInPlayerGo.AddComponent<ConvaiPlayer>();
            builtInPlayer.Configure("Built In Player", "built-in-speaker-id");

            var customPlayerGo = new GameObject("CustomPlayer");
            _createdObjects.Add(customPlayerGo);
            var customPlayer = customPlayerGo.AddComponent<CustomPlayerAgent>();
            customPlayer.Configure("Custom Player", "custom-speaker-id");

            var locator = new ConvaiCharacterLocatorService();

            IReadOnlyList<IConvaiCharacterAgent> characterAgents = locator.GetCharacterAgents();
            IReadOnlyList<IConvaiPlayerAgent> playerAgents = locator.GetPlayerAgents();

            Assert.That(characterAgents, Has.Member(builtInCharacter));
            Assert.That(characterAgents, Has.Member(customCharacter));
            Assert.That(playerAgents, Has.Member(builtInPlayer));
            Assert.That(playerAgents, Has.Member(customPlayer));

            Assert.IsTrue(locator.TryGetCharacter("custom-character-id", out IConvaiCharacterAgent foundCharacter));
            Assert.AreSame(customCharacter, foundCharacter);

            Assert.IsTrue(locator.TryGetPlayer("custom-speaker-id", out IConvaiPlayerAgent foundPlayer));
            Assert.AreSame(customPlayer, foundPlayer);
        }

        private sealed class CustomPlayerAgent : MonoBehaviour, IConvaiPlayerAgent
        {
            [SerializeField] private string _playerName = "Custom Player";
            [SerializeField] private string _speakerId = "custom-player-speaker";

            public string PlayerName => _playerName;
            public string SpeakerId => _speakerId;
            public Color NameTagColor => Color.cyan;

            public event Action<string> OnTextMessageSent;

            public void SendTextMessage(string message) => OnTextMessageSent?.Invoke(message);

            public void Configure(string playerName, string speakerId)
            {
                _playerName = playerName;
                _speakerId = speakerId;
            }
        }

        private sealed class CustomCharacterAgent : MonoBehaviour, IConvaiCharacterAgent
        {
            [SerializeField] private string _characterId = "custom-character";
            [SerializeField] private string _characterName = "Custom Character";

            public string CharacterId => _characterId;
            public string CharacterName => _characterName;
            public Color NameTagColor => Color.magenta;
            public bool EnableSessionResume => false;

            public void SendTrigger(string triggerName, string triggerMessage = null) { }
            public void SendDynamicInfo(string contextText) { }
            public void UpdateTemplateKeys(Dictionary<string, string> templateKeys) { }

            public void Configure(string characterId, string characterName)
            {
                _characterId = characterId;
                _characterName = characterName;
            }
        }

        private sealed class TestCharacterRegistry : ICharacterRegistry, ICharacterAudioSourceResolver
        {
            private readonly Dictionary<string, AudioSource> _audioSources = new();
            private readonly Dictionary<string, CharacterDescriptor> _descriptors = new();

            public bool TryGetAudioSource(string characterId, out AudioSource audioSource) =>
                _audioSources.TryGetValue(characterId, out audioSource);

            public void SetAudioSource(string characterId, AudioSource audioSource) =>
                _audioSources[characterId] = audioSource;

            public void RegisterCharacter(CharacterDescriptor descriptor) =>
                _descriptors[descriptor.CharacterId] = descriptor;

            public void UnregisterCharacter(string characterId)
            {
                _descriptors.Remove(characterId);
                _audioSources.Remove(characterId);
            }

            public bool TryGetCharacter(string characterId, out CharacterDescriptor descriptor) =>
                _descriptors.TryGetValue(characterId, out descriptor);

            public bool TryGetCharacterByParticipantId(string participantId, out CharacterDescriptor descriptor)
            {
                foreach (CharacterDescriptor item in _descriptors.Values)
                {
                    if (item.ParticipantId == participantId)
                    {
                        descriptor = item;
                        return true;
                    }
                }

                descriptor = default;
                return false;
            }

            public IReadOnlyList<CharacterDescriptor> GetAllCharacters() => _descriptors.Values.ToList();

            public void SetCharacterMuted(string characterId, bool muted)
            {
                if (_descriptors.TryGetValue(characterId, out CharacterDescriptor descriptor))
                    _descriptors[characterId] = descriptor.WithMuteState(muted);
            }

            public void Clear()
            {
                _descriptors.Clear();
                _audioSources.Clear();
            }
        }
    }
}
