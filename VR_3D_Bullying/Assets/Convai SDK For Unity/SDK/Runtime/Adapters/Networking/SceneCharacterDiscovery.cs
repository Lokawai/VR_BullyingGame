using System;
using System.Collections.Generic;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Runtime.Utilities;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Unity scene discovery service for finding Convai characters and players.
    /// </summary>
    /// <remarks>
    ///     Extracted from ConvaiRoomManager to adhere to Single Responsibility Principle.
    ///     Handles:
    ///     - Finding IConvaiPlayerAgent implementations in scene
    ///     - Finding IConvaiCharacterAgent implementations in scene
    ///     - AudioSource initialization for characters
    ///     - CharacterDescriptor creation and registry registration
    /// </remarks>
    public sealed class SceneCharacterDiscovery
    {
        private readonly ILogger _logger;
        private readonly Action<string, bool> _onRemoteAudioPreferenceDiscovered;

        /// <summary>
        ///     Creates a new SceneCharacterDiscovery instance.
        /// </summary>
        /// <param name="logger">Logger for diagnostic messages (can be null).</param>
        /// <param name="onRemoteAudioPreferenceDiscovered">
        ///     Optional callback invoked for each discovered character with (characterId, enableRemoteAudio).
        ///     Used to initialize remote audio preferences.
        /// </param>
        public SceneCharacterDiscovery(
            ILogger logger = null,
            Action<string, bool> onRemoteAudioPreferenceDiscovered = null)
        {
            _logger = logger;
            CharacterToParticipantMap = new Dictionary<IConvaiCharacterAgent, CharacterAudioData>();
            _onRemoteAudioPreferenceDiscovered = onRemoteAudioPreferenceDiscovered;
        }

        /// <inheritdoc />
        public Dictionary<IConvaiCharacterAgent, CharacterAudioData> CharacterToParticipantMap { get; }

        /// <inheritdoc />
        public IConvaiPlayerAgent DiscoverPlayer()
        {
            IReadOnlyList<IConvaiPlayerAgent> playerAgents =
                InterfaceComponentQuery.FindObjects<IConvaiPlayerAgent>();

            if (playerAgents.Count > 0)
            {
                IConvaiPlayerAgent agent = playerAgents[0];
                _logger?.Info($"[SceneCharacterDiscovery] Found player: {agent.PlayerName}");
                return agent;
            }

            _logger?.Error("[SceneCharacterDiscovery] No IConvaiPlayerAgent found in the scene");
            return null;
        }

        /// <inheritdoc />
        public List<IConvaiCharacterAgent> DiscoverCharacters(ICharacterRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            CharacterToParticipantMap.Clear();
            var characterList = new List<IConvaiCharacterAgent>();

            IReadOnlyList<IConvaiCharacterAgent> characterAgents =
                InterfaceComponentQuery.FindObjects<IConvaiCharacterAgent>();

            for (int i = 0; i < characterAgents.Count; i++)
            {
                IConvaiCharacterAgent characterAgent = characterAgents[i];
                if (characterAgent is not MonoBehaviour mb) continue;

                characterList.Add(characterAgent);
                CharacterToParticipantMap[characterAgent] = new CharacterAudioData();

                _onRemoteAudioPreferenceDiscovered?.Invoke(characterAgent.CharacterId, false);

                if (mb is ConvaiCharacter convaiCharacter && convaiCharacter.EnableRemoteAudioOnStart)
                    _onRemoteAudioPreferenceDiscovered?.Invoke(characterAgent.CharacterId, true);

                AudioSource audioSource = InitializeAudioSource(mb);
                if (CharacterToParticipantMap.TryGetValue(characterAgent, out CharacterAudioData characterData))
                {
                    characterData.AudioSource = audioSource;
                    characterData.ParticipantId = string.Empty;
                    characterData.IsMuted = false;
                }

                var descriptor = new CharacterDescriptor(
                    mb.GetInstanceID().ToString(),
                    characterAgent.CharacterId,
                    characterAgent.CharacterName,
                    string.Empty,
                    false);

                registry.RegisterCharacter(descriptor);

                if (registry is ICharacterAudioSourceResolver resolver)
                    resolver.SetAudioSource(characterAgent.CharacterId, audioSource);

                _logger?.Info(
                    $"[SceneCharacterDiscovery] Found Character: {characterAgent.CharacterName} (ID: {characterAgent.CharacterId})");
            }

            if (characterList.Count == 0)
                _logger?.Error("[SceneCharacterDiscovery] No IConvaiCharacterAgent found in the scene");
            else
                _logger?.Info($"[SceneCharacterDiscovery] Discovered {characterList.Count} Character(s)");

            return characterList;
        }

        /// <summary>
        ///     Initializes an AudioSource component on the character GameObject.
        /// </summary>
        private AudioSource InitializeAudioSource(MonoBehaviour characterMb)
        {
            var audioSource = characterMb.GetComponent<AudioSource>();
            if (audioSource == null) audioSource = characterMb.gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.volume = 1f;
            audioSource.priority = 128;
            audioSource.spatialBlend = 0f;
            audioSource.Stop();
            audioSource.clip = null;
            audioSource.mute = false;

            return audioSource;
        }
    }
}
