using System;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.Runtime.Networking.Media;
using NUnit.Framework;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode.Runtime
{
    /// <summary>
    ///     Unit tests for AudioTrackManager covering publishing, subscription,
    ///     mute functionality, character audio routing, and cleanup scenarios.
    /// </summary>
    [TestFixture]
    public class AudioTrackManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            _logger = new TestLogger();
            _characterRegistry = new TestCharacterRegistry();
            _audioStreamFactory = new TestAudioStreamFactory();
            _audioSources = new Dictionary<string, AudioSource>();
            _createdObjects = new List<GameObject>();
            _nullRoom = null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _createdObjects)
                if (go != null)
                    Object.DestroyImmediate(go);
            _createdObjects.Clear();
            _audioSources.Clear();
            _audioStreamFactory.Dispose();
        }

        private sealed class TestLogger : ILogger
        {
            public List<string> DebugMessages { get; } = new();
            public List<string> InfoMessages { get; } = new();
            public List<string> WarningMessages { get; } = new();
            public List<string> ErrorMessages { get; } = new();
            public bool DebugEnabled { get; set; } = true;

            public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) { }

            public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Debug(string message, LogCategory category = LogCategory.SDK) => DebugMessages.Add(message);

            public void Debug(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Debug(message, category);

            public void Info(string message, LogCategory category = LogCategory.SDK) => InfoMessages.Add(message);

            public void Info(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Info(message, category);

            public void Warning(string message, LogCategory category = LogCategory.SDK) => WarningMessages.Add(message);

            public void Warning(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Warning(message, category);

            public void Error(string message, LogCategory category = LogCategory.SDK) => ErrorMessages.Add(message);

            public void Error(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Error(message, category);

            public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK) =>
                ErrorMessages.Add(message ?? exception.Message);

            public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Error(exception, message, category);

            public bool IsEnabled(LogLevel level, LogCategory category) => level != LogLevel.Debug || DebugEnabled;
        }

        private sealed class TestCharacterRegistry : ICharacterRegistry
        {
            private readonly Dictionary<string, CharacterDescriptor> _characters = new();
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

            public bool TryGetCharacter(string characterId, out CharacterDescriptor descriptor) =>
                _characters.TryGetValue(characterId, out descriptor);

            public bool TryGetCharacterByParticipantId(string participantId, out CharacterDescriptor descriptor) =>
                _participantMap.TryGetValue(participantId, out descriptor);

            public IReadOnlyList<CharacterDescriptor> GetAllCharacters() =>
                new List<CharacterDescriptor>(_characters.Values);

            public void SetCharacterMuted(string characterId, bool muted)
            {
                if (_characters.TryGetValue(characterId, out CharacterDescriptor desc))
                    _characters[characterId] = desc.WithMuteState(muted);
            }

            public void Clear()
            {
                _characters.Clear();
                _participantMap.Clear();
            }
        }

        private sealed class TestAudioStreamFactory : IAudioStreamFactory, IDisposable
        {
            public int CreateCallCount { get; private set; }
            public int DisposeCallCount { get; private set; }
            public bool ReturnNull { get; set; }

            public IDisposable Create(IRemoteAudioTrack track, AudioSource source)
            {
                CreateCallCount++;
                if (ReturnNull) return null;
                return new TestAudioStream(() => DisposeCallCount++);
            }

            public void Dispose() { }
        }

        private sealed class TestAudioStream : IDisposable
        {
            private readonly Action _onDispose;

            public TestAudioStream(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose() => _onDispose?.Invoke();
        }

        private TestLogger _logger;
        private TestCharacterRegistry _characterRegistry;
        private TestAudioStreamFactory _audioStreamFactory;
        private Dictionary<string, AudioSource> _audioSources;
        private List<GameObject> _createdObjects;
        private IRoomFacade _nullRoom;

        private AudioSource CreateAudioSource(string characterId)
        {
            var go = new GameObject($"AudioSource_{characterId}");
            _createdObjects.Add(go);
            var source = go.AddComponent<AudioSource>();
            _audioSources[characterId] = source;
            return source;
        }

        private AudioTrackManager CreateManager(Func<IRoomFacade> roomFacadeProvider = null)
        {
            return new AudioTrackManager(
                roomFacadeProvider ?? (() => _nullRoom),
                _characterRegistry,
                _logger,
                characterId => _audioSources.TryGetValue(characterId, out AudioSource src) ? src : null,
                null,
                _audioStreamFactory
            );
        }

        [Test]
        public void AudioTrackManager_ImplementsIAudioTrackManager()
        {
            using AudioTrackManager manager = CreateManager();
            Assert.IsTrue(manager is IAudioTrackManager);
        }

        [Test]
        public void Constructor_ThrowsOnNullRoomProvider()
        {
            Assert.Throws<ArgumentNullException>(() => new AudioTrackManager(
                null,
                _characterRegistry,
                _logger,
                id => null
            ));
        }

        [Test]
        public void Constructor_ThrowsOnNullCharacterRegistry()
        {
            Assert.Throws<ArgumentNullException>(() => new AudioTrackManager(
                () => _nullRoom,
                null,
                _logger,
                id => null
            ));
        }

        [Test]
        public void Constructor_ThrowsOnNullAudioSourceResolver()
        {
            Assert.Throws<ArgumentNullException>(() => new AudioTrackManager(
                () => _nullRoom,
                _characterRegistry,
                _logger,
                null
            ));
        }

        [Test]
        public void IsMicMuted_DefaultsFalse()
        {
            using AudioTrackManager manager = CreateManager();
            Assert.IsFalse(manager.IsMicMuted);
        }

        [Test]
        public void SetMicMuted_UpdatesState()
        {
            using AudioTrackManager manager = CreateManager();
            manager.SetMicMuted(true);
            Assert.IsTrue(manager.IsMicMuted);

            manager.SetMicMuted(false);
            Assert.IsFalse(manager.IsMicMuted);
        }

        [Test]
        public void SetMicMuted_RaisesEventOnChange()
        {
            using AudioTrackManager manager = CreateManager();
            bool? eventValue = null;
            manager.OnMicMuteChanged += muted => eventValue = muted;

            manager.SetMicMuted(true);
            Assert.AreEqual(true, eventValue);

            manager.SetMicMuted(false);
            Assert.AreEqual(false, eventValue);
        }

        [Test]
        public void SetMicMuted_DoesNotRaiseEventWhenUnchanged()
        {
            using AudioTrackManager manager = CreateManager();
            int eventCount = 0;
            manager.OnMicMuteChanged += _ => eventCount++;

            manager.SetMicMuted(false);
            Assert.AreEqual(0, eventCount, "Should not raise event when state unchanged");
        }

        [Test]
        public void ToggleMicMute_TogglesState()
        {
            using AudioTrackManager manager = CreateManager();
            Assert.IsFalse(manager.IsMicMuted);

            manager.ToggleMicMute();
            Assert.IsTrue(manager.IsMicMuted);

            manager.ToggleMicMute();
            Assert.IsFalse(manager.IsMicMuted);
        }

        [Test]
        public void SetCharacterAudioMuted_ReturnsFalseForNullCharacterId()
        {
            using AudioTrackManager manager = CreateManager();
            Assert.IsFalse(manager.SetCharacterAudioMuted(null, true));
            Assert.IsFalse(manager.SetCharacterAudioMuted("", true));
        }

        [Test]
        public void SetCharacterAudioMuted_ReturnsFalseForUnregisteredCharacter()
        {
            using AudioTrackManager manager = CreateManager();
            Assert.IsFalse(manager.SetCharacterAudioMuted("unknown-character", true));
        }

        [Test]
        public void SetCharacterAudioMuted_UpdatesRegisteredCharacter()
        {
            using AudioTrackManager manager = CreateManager();
            string characterId = "test-char-1";
            AudioSource audioSource = CreateAudioSource(characterId);
            var descriptor = new CharacterDescriptor("inst-1", characterId, "Test", "", false);
            _characterRegistry.RegisterCharacter(descriptor);

            bool result = manager.SetCharacterAudioMuted(characterId, true);

            Assert.IsTrue(result);
            Assert.IsTrue(audioSource.mute);
        }

        [Test]
        public void IsCharacterAudioMuted_ReturnsFalseForNullCharacterId()
        {
            using AudioTrackManager manager = CreateManager();
            Assert.IsFalse(manager.IsCharacterAudioMuted(null));
            Assert.IsFalse(manager.IsCharacterAudioMuted(""));
        }

        [Test]
        public void IsCharacterAudioMuted_ReturnsCorrectState()
        {
            using AudioTrackManager manager = CreateManager();
            string characterId = "test-char-1";
            CreateAudioSource(characterId);
            var descriptor = new CharacterDescriptor("inst-1", characterId, "Test", "", true);
            _characterRegistry.RegisterCharacter(descriptor);

            Assert.IsTrue(manager.IsCharacterAudioMuted(characterId));
        }

        [Test]
        public void ClearState_ClearsRemoteAudio()
        {
            using AudioTrackManager manager = CreateManager();
            string characterId = "test-char-1";
            AudioSource audioSource = CreateAudioSource(characterId);
            var descriptor = new CharacterDescriptor("inst-1", characterId, "Test", "", false);
            _characterRegistry.RegisterCharacter(descriptor);

            manager.HandleRemoteAudioTrackSubscribed(null, "participant-1", characterId);

            Assert.AreEqual(1, _audioStreamFactory.CreateCallCount, "Stream should be created");

            manager.ClearState();

            Assert.AreEqual(1, _audioStreamFactory.DisposeCallCount, "ClearState should dispose audio streams");
        }

        [Test]
        public void ClearRemoteAudio_DisposesAllStreams()
        {
            using AudioTrackManager manager = CreateManager();

            for (int i = 1; i <= 3; i++)
            {
                string characterId = $"test-char-{i}";
                CreateAudioSource(characterId);
                var descriptor = new CharacterDescriptor($"inst-{i}", characterId, $"Test{i}", "", false);
                _characterRegistry.RegisterCharacter(descriptor);
                manager.HandleRemoteAudioTrackSubscribed(null, $"participant-{i}", characterId);
            }

            Assert.AreEqual(3, _audioStreamFactory.CreateCallCount);

            manager.ClearRemoteAudio();

            Assert.AreEqual(3, _audioStreamFactory.DisposeCallCount, "All streams should be disposed");
        }

        [Test]
        public void Dispose_ClearsAllResources()
        {
            AudioTrackManager manager = CreateManager();
            string characterId = "test-char-1";
            CreateAudioSource(characterId);
            var descriptor = new CharacterDescriptor("inst-1", characterId, "Test", "", false);
            _characterRegistry.RegisterCharacter(descriptor);
            manager.HandleRemoteAudioTrackSubscribed(null, "participant-1", characterId);

            manager.Dispose();

            Assert.AreEqual(1, _audioStreamFactory.DisposeCallCount);

            Assert.Throws<ObjectDisposedException>(() => manager.SetMicMuted(true));
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            AudioTrackManager manager = CreateManager();
            manager.Dispose();
            manager.Dispose();
        }

        [Test]
        public void HandleRemoteAudioTrackSubscribed_CreatesAudioStream()
        {
            using AudioTrackManager manager = CreateManager();
            string characterId = "test-char-1";
            CreateAudioSource(characterId);
            var descriptor = new CharacterDescriptor("inst-1", characterId, "Test", "", false);
            _characterRegistry.RegisterCharacter(descriptor);

            manager.HandleRemoteAudioTrackSubscribed(null, "participant-1", characterId);

            Assert.AreEqual(1, _audioStreamFactory.CreateCallCount);
        }

        [Test]
        public void HandleRemoteAudioTrackSubscribed_LogsErrorWhenCharacterNotFound()
        {
            using AudioTrackManager manager = CreateManager();

            manager.HandleRemoteAudioTrackSubscribed(null, "participant-1", "unknown-character");

            Assert.IsTrue(_logger.ErrorMessages.Count > 0);
            Assert.IsTrue(_logger.ErrorMessages[0].Contains("FAILED to resolve Character"));
        }

        [Test]
        public void HandleRemoteAudioTrackSubscribed_LogsErrorWhenNoAudioSource()
        {
            using AudioTrackManager manager = CreateManager();
            string characterId = "test-char-1";
            var descriptor = new CharacterDescriptor("inst-1", characterId, "Test", "", false);
            _characterRegistry.RegisterCharacter(descriptor);

            manager.HandleRemoteAudioTrackSubscribed(null, "participant-1", characterId);

            Assert.IsTrue(_logger.ErrorMessages.Count > 0);
            Assert.IsTrue(_logger.ErrorMessages[0].Contains("does not have an AudioSource"));
        }

        [Test]
        public void HandleRemoteAudioTrackSubscribed_DisposesExistingStream()
        {
            using AudioTrackManager manager = CreateManager();
            string characterId = "test-char-1";
            CreateAudioSource(characterId);
            var descriptor = new CharacterDescriptor("inst-1", characterId, "Test", "", false);
            _characterRegistry.RegisterCharacter(descriptor);

            manager.HandleRemoteAudioTrackSubscribed(null, "participant-1", characterId);
            Assert.AreEqual(1, _audioStreamFactory.CreateCallCount);
            Assert.AreEqual(0, _audioStreamFactory.DisposeCallCount);

            manager.HandleRemoteAudioTrackSubscribed(null, "participant-2", characterId);
            Assert.AreEqual(2, _audioStreamFactory.CreateCallCount);
            Assert.AreEqual(1, _audioStreamFactory.DisposeCallCount);
        }

        [Test]
        public void HandleRemoteAudioTrackUnsubscribed_DisposesStream()
        {
            using AudioTrackManager manager = CreateManager();
            string characterId = "test-char-1";
            AudioSource audioSource = CreateAudioSource(characterId);
            var descriptor = new CharacterDescriptor("inst-1", characterId, "Test", "participant-1", false);
            _characterRegistry.RegisterCharacter(descriptor);

            manager.HandleRemoteAudioTrackSubscribed(null, "participant-1", characterId);
            manager.HandleRemoteAudioTrackUnsubscribed("participant-1");

            Assert.AreEqual(1, _audioStreamFactory.DisposeCallCount);
            Assert.IsFalse(audioSource.isPlaying);
        }

        [Test]
        public void HandleRemoteAudioTrackSubscribed_SkipsDebugLoggingWhenDisabled()
        {
            _logger.DebugEnabled = false;
            using AudioTrackManager manager = CreateManager();
            string characterId = "test-char-1";
            CreateAudioSource(characterId);
            var descriptor = new CharacterDescriptor("inst-1", characterId, "Test", "", false);
            _characterRegistry.RegisterCharacter(descriptor);

            manager.HandleRemoteAudioTrackSubscribed(null, "participant-1", characterId);

            Assert.AreEqual(0, _logger.DebugMessages.Count,
                "Debug logs should be skipped when debug level is disabled");
        }

        [Test]
        public void HandleRemoteAudioTrackSubscribed_LogsDebugWhenEnabled()
        {
            _logger.DebugEnabled = true;
            using AudioTrackManager manager = CreateManager();
            string characterId = "test-char-1";
            CreateAudioSource(characterId);
            var descriptor = new CharacterDescriptor("inst-1", characterId, "Test", "", false);
            _characterRegistry.RegisterCharacter(descriptor);

            manager.HandleRemoteAudioTrackSubscribed(null, "participant-1", characterId);

            Assert.IsTrue(_logger.DebugMessages.Count > 0,
                "Debug logs should be present when debug level is enabled");
        }

        [Test]
        public void IsPublishing_DefaultsFalse()
        {
            using AudioTrackManager manager = CreateManager();
            Assert.IsFalse(manager.IsPublishing);
        }
    }
}
