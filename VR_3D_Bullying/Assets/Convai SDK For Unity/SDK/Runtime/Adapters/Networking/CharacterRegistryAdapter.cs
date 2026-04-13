using System.Collections.Generic;
using System.Linq;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.Runtime.Behaviors;
using UnityEngine;

namespace Convai.Runtime.Adapters.Networking
{
    /// <summary>
    ///     Adapter for ICharacterRegistry that bridges IConvaiCharacterAgent to the Transport layer.
    ///     Also implements ICharacterAudioSourceResolver to provide AudioSource resolution for characters.
    /// </summary>
    internal class CharacterRegistryAdapter : ICharacterRegistry, ICharacterAudioSourceResolver
    {
        private readonly Dictionary<string, IConvaiCharacterAgent> _characterLookup;
        private readonly Dictionary<IConvaiCharacterAgent, CharacterAudioData> _characterToParticipantMap;
        private readonly Dictionary<string, CharacterDescriptor> _descriptors;
        private readonly Dictionary<string, string> _participantToCharacterMap;
        private readonly object _syncRoot = new();

        /// <summary>
        ///     Initializes a new instance of the <see cref="CharacterRegistryAdapter" /> class.
        /// </summary>
        /// <param name="characterToParticipantMap">Map of character agents to participant audio data.</param>
        /// <param name="characterList">Initial list of characters to register.</param>
        public CharacterRegistryAdapter(Dictionary<IConvaiCharacterAgent, CharacterAudioData> characterToParticipantMap,
            List<IConvaiCharacterAgent> characterList)
        {
            _characterToParticipantMap =
                characterToParticipantMap ?? new Dictionary<IConvaiCharacterAgent, CharacterAudioData>();
            _characterLookup = new Dictionary<string, IConvaiCharacterAgent>();
            _descriptors = new Dictionary<string, CharacterDescriptor>();
            _participantToCharacterMap = new Dictionary<string, string>();

            if (characterList == null) return;

            foreach (IConvaiCharacterAgent character in characterList)
            {
                if (character == null || string.IsNullOrEmpty(character.CharacterId)) continue;
                _characterLookup[character.CharacterId] = character;
                if (!_characterToParticipantMap.ContainsKey(character))
                    _characterToParticipantMap[character] = new CharacterAudioData();
            }
        }

        /// <inheritdoc />
        public void RegisterCharacter(CharacterDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(descriptor.CharacterId)) return;
            lock (_syncRoot) RegisterCharacterInternal(descriptor);
        }

        /// <inheritdoc />
        public void UnregisterCharacter(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return;
            lock (_syncRoot)
            {
                if (!_descriptors.TryGetValue(characterId, out CharacterDescriptor descriptor)) return;
                _descriptors.Remove(characterId);
                if (!string.IsNullOrEmpty(descriptor.ParticipantId))
                    _participantToCharacterMap.Remove(descriptor.ParticipantId);

                if (_characterLookup.TryGetValue(characterId, out IConvaiCharacterAgent character) &&
                    _characterToParticipantMap.TryGetValue(character, out CharacterAudioData data))
                    CleanupCharacterData(data);
            }
        }

        /// <inheritdoc />
        public bool TryGetCharacter(string characterId, out CharacterDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                descriptor = default;
                return false;
            }

            lock (_syncRoot) return _descriptors.TryGetValue(characterId, out descriptor);
        }

        /// <inheritdoc />
        public bool TryGetCharacterByParticipantId(string participantId, out CharacterDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(participantId))
            {
                descriptor = default;
                return false;
            }

            lock (_syncRoot)
            {
                if (_participantToCharacterMap.TryGetValue(participantId, out string characterId) &&
                    _descriptors.TryGetValue(characterId, out descriptor))
                    return true;
            }

            descriptor = default;
            return false;
        }

        /// <inheritdoc />
        public IReadOnlyList<CharacterDescriptor> GetAllCharacters()
        {
            lock (_syncRoot) return _descriptors.Values.ToList();
        }

        /// <inheritdoc />
        public void SetCharacterMuted(string characterId, bool muted)
        {
            lock (_syncRoot)
            {
                if (string.IsNullOrEmpty(characterId)) return;
                if (!_descriptors.TryGetValue(characterId, out CharacterDescriptor descriptor) ||
                    descriptor.IsMuted == muted) return;
                RegisterCharacterInternal(descriptor.WithMuteState(muted));
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            lock (_syncRoot)
            {
                List<CharacterDescriptor> descriptorsToReset = _descriptors.Values
                    .Select(d => d.WithParticipantId(string.Empty).WithMuteState(false)).ToList();
                _participantToCharacterMap.Clear();
                foreach (CharacterDescriptor reset in descriptorsToReset) RegisterCharacterInternal(reset);

                foreach (CharacterAudioData data in _characterToParticipantMap.Values)
                {
                    if (data.AudioSource != null)
                        data.AudioSource.mute = false;
                }
            }
        }

        private void CleanupCharacterData(CharacterAudioData data)
        {
            data.ParticipantId = string.Empty;
            data.IsMuted = false;
            if (data.AudioSource != null)
            {
                data.AudioSource.mute = false;
                data.AudioSource.Stop();
                data.AudioSource.clip = null;
            }

            if (data.AudioStream != null)
            {
                data.AudioStream.Dispose();
                data.AudioStream = null;
            }
        }

        private void RegisterCharacterInternal(CharacterDescriptor descriptor)
        {
            if (_descriptors.TryGetValue(descriptor.CharacterId, out CharacterDescriptor existing) &&
                !string.IsNullOrEmpty(existing.ParticipantId) &&
                existing.ParticipantId != descriptor.ParticipantId)
                _participantToCharacterMap.Remove(existing.ParticipantId);

            if (TryResolveCharacter(descriptor.CharacterId, out IConvaiCharacterAgent character))
            {
                if (!_characterToParticipantMap.TryGetValue(character, out CharacterAudioData data))
                {
                    data = new CharacterAudioData();
                    _characterToParticipantMap[character] = data;
                }

                data.ParticipantId = descriptor.ParticipantId ?? string.Empty;
                data.IsMuted = descriptor.IsMuted;

                AudioSource resolvedSource = data.AudioSource;
                if (resolvedSource == null && character is MonoBehaviour mb &&
                    mb.TryGetComponent(out AudioSource existingSource))
                {
                    resolvedSource = existingSource;
                    data.AudioSource = resolvedSource;
                }

                if (resolvedSource != null) resolvedSource.mute = descriptor.IsMuted;
            }

            _descriptors[descriptor.CharacterId] = descriptor;

            if (!string.IsNullOrEmpty(descriptor.ParticipantId))
                _participantToCharacterMap[descriptor.ParticipantId] = descriptor.CharacterId;
        }

        private bool TryResolveCharacter(string characterId, out IConvaiCharacterAgent character)
        {
            character = null;
            if (string.IsNullOrEmpty(characterId)) return false;

            if (_characterLookup.TryGetValue(characterId, out character)) return true;

            character = _characterToParticipantMap.Keys.FirstOrDefault(candidate =>
                candidate != null && candidate.CharacterId == characterId);
            if (character == null) return false;

            _characterLookup[characterId] = character;
            return true;
        }

        #region ICharacterAudioSourceResolver Implementation

        /// <inheritdoc />
        public bool TryGetAudioSource(string characterId, out AudioSource audioSource)
        {
            audioSource = null;
            if (string.IsNullOrEmpty(characterId)) return false;

            lock (_syncRoot)
            {
                if (!TryResolveCharacter(characterId, out IConvaiCharacterAgent character))
                    return false;

                if (_characterToParticipantMap.TryGetValue(character, out CharacterAudioData data) &&
                    data.AudioSource != null)
                {
                    audioSource = data.AudioSource;
                    return true;
                }

                if (character is MonoBehaviour mb && mb.TryGetComponent(out AudioSource existingSource))
                {
                    audioSource = existingSource;
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public void SetAudioSource(string characterId, AudioSource audioSource)
        {
            if (string.IsNullOrEmpty(characterId)) return;

            lock (_syncRoot)
            {
                if (!TryResolveCharacter(characterId, out IConvaiCharacterAgent character))
                    return;

                if (!_characterToParticipantMap.TryGetValue(character, out CharacterAudioData data))
                {
                    data = new CharacterAudioData();
                    _characterToParticipantMap[character] = data;
                }

                data.AudioSource = audioSource;
            }
        }

        #endregion
    }
}
