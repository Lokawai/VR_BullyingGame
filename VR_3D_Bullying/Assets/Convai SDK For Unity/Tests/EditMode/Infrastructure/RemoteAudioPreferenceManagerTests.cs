using System.Collections.Generic;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Audio;
using Convai.Infrastructure.Networking.Models;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Infrastructure
{
    /// <summary>
    ///     Unit tests for RemoteAudioPreferenceManager.
    ///     Tests the three-strategy resolution in ShouldSubscribe and preference management.
    /// </summary>
    [TestFixture]
    public class RemoteAudioPreferenceManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            _mockRegistry = new MockCharacterRegistry();
            _manager = new RemoteAudioPreferenceManager(_mockRegistry);
        }

        private RemoteAudioPreferenceManager _manager;
        private MockCharacterRegistry _mockRegistry;

        /// <summary>
        ///     Mock implementation of ICharacterRegistry for testing.
        ///     Supports identity-to-character mapping for Strategy 1 tests.
        /// </summary>
        private sealed class MockCharacterRegistry : ICharacterRegistry
        {
            private readonly Dictionary<string, CharacterDescriptor> _characters = new();
            private readonly Dictionary<string, CharacterDescriptor> _identityMap = new();
            private readonly Dictionary<string, CharacterDescriptor> _participantMap = new();

            public void RegisterCharacter(CharacterDescriptor descriptor)
            {
                _characters[descriptor.CharacterId] = descriptor;
                if (!string.IsNullOrEmpty(descriptor.ParticipantId))
                    _participantMap[descriptor.ParticipantId] = descriptor;
            }

            public void UnregisterCharacter(string characterId)
            {
                if (_characters.TryGetValue(characterId, out CharacterDescriptor desc))
                {
                    _characters.Remove(characterId);
                    if (!string.IsNullOrEmpty(desc.ParticipantId)) _participantMap.Remove(desc.ParticipantId);
                }
            }

            public bool TryGetCharacter(string characterId, out CharacterDescriptor descriptor)
            {
                if (_identityMap.TryGetValue(characterId, out descriptor)) return true;
                return _characters.TryGetValue(characterId, out descriptor);
            }

            public bool TryGetCharacterByParticipantId(string participantId, out CharacterDescriptor descriptor) =>
                _participantMap.TryGetValue(participantId, out descriptor);

            public IReadOnlyList<CharacterDescriptor> GetAllCharacters() =>
                new List<CharacterDescriptor>(_characters.Values);

            public void SetCharacterMuted(string characterId, bool muted) { }
            public void Clear() => _characters.Clear();

            /// <summary>
            ///     Registers a character with a specific identity mapping (for Strategy 1 testing).
            /// </summary>
            public void RegisterWithIdentity(string identity, CharacterDescriptor descriptor)
            {
                RegisterCharacter(descriptor);
                _identityMap[identity] = descriptor;
            }
        }

        [Test]
        public void IsRemoteAudioEnabled_NullCharacterId_ReturnsFalse() =>
            Assert.IsFalse(_manager.IsRemoteAudioEnabled(null));

        [Test]
        public void IsRemoteAudioEnabled_EmptyCharacterId_ReturnsFalse() =>
            Assert.IsFalse(_manager.IsRemoteAudioEnabled(string.Empty));

        [Test]
        public void IsRemoteAudioEnabled_UnknownCharacter_ReturnsFalse() =>
            Assert.IsFalse(_manager.IsRemoteAudioEnabled("unknown-char"));

        [Test]
        public void IsRemoteAudioEnabled_AfterSetEnabled_ReturnsTrue()
        {
            _manager.SetRemoteAudioEnabled("char-1", true);
            Assert.IsTrue(_manager.IsRemoteAudioEnabled("char-1"));
        }

        [Test]
        public void IsRemoteAudioEnabled_AfterSetDisabled_ReturnsFalse()
        {
            _manager.SetRemoteAudioEnabled("char-1", true);
            _manager.SetRemoteAudioEnabled("char-1", false);
            Assert.IsFalse(_manager.IsRemoteAudioEnabled("char-1"));
        }

        [Test]
        public void ShouldSubscribe_NullIdentity_ReturnsFalse() => Assert.IsFalse(_manager.ShouldSubscribe(null));

        [Test]
        public void ShouldSubscribe_EmptyIdentity_ReturnsFalse() =>
            Assert.IsFalse(_manager.ShouldSubscribe(string.Empty));

        [Test]
        public void ShouldSubscribe_IdentityMatchesRegisteredCharacter_UsesCharacterPreference()
        {
            var descriptor = new CharacterDescriptor("inst-1", "char-123", "Test Character", "", false);
            _mockRegistry.RegisterWithIdentity("ConvAI-Bot", descriptor);

            _manager.SetRemoteAudioEnabled("char-123", false);
            Assert.IsFalse(_manager.ShouldSubscribe("ConvAI-Bot"));

            _manager.SetRemoteAudioEnabled("char-123", true);
            Assert.IsTrue(_manager.ShouldSubscribe("ConvAI-Bot"));
        }

        [Test]
        public void ShouldSubscribe_IdentityIsCharacterId_UsesDirectPreference()
        {
            _manager.SetRemoteAudioEnabled("char-direct", true);
            Assert.IsTrue(_manager.ShouldSubscribe("char-direct"));
        }

        [Test]
        public void ShouldSubscribe_UnknownIdentity_NoPreferences_ReturnsTrue() =>
            Assert.IsTrue(_manager.ShouldSubscribe("ConvAI-Bot"));

        [Test]
        public void ShouldSubscribe_UnknownIdentity_AnyCharacterEnabled_ReturnsTrue()
        {
            _manager.SetRemoteAudioEnabled("char-1", false);
            _manager.SetRemoteAudioEnabled("char-2", true);

            Assert.IsTrue(_manager.ShouldSubscribe("ConvAI-Bot"));
        }

        [Test]
        public void ShouldSubscribe_UnknownIdentity_AllCharactersDisabled_ReturnsFalse()
        {
            _manager.SetRemoteAudioEnabled("char-1", false);
            _manager.SetRemoteAudioEnabled("char-2", false);

            Assert.IsFalse(_manager.ShouldSubscribe("ConvAI-Bot"));
        }

        [Test]
        public void SetRemoteAudioEnabled_RaisesEvent_OnChange()
        {
            string eventCharId = null;
            bool eventEnabled = false;
            _manager.RemoteAudioEnabledChanged += (charId, enabled) =>
            {
                eventCharId = charId;
                eventEnabled = enabled;
            };

            _manager.SetRemoteAudioEnabled("char-1", true);

            Assert.AreEqual("char-1", eventCharId);
            Assert.IsTrue(eventEnabled);
        }

        [Test]
        public void SetRemoteAudioEnabled_DoesNotRaiseEvent_WhenNoChange()
        {
            _manager.SetRemoteAudioEnabled("char-1", true);

            int eventCount = 0;
            _manager.RemoteAudioEnabledChanged += (_, _) => eventCount++;

            _manager.SetRemoteAudioEnabled("char-1", true);

            Assert.AreEqual(0, eventCount);
        }
    }
}
