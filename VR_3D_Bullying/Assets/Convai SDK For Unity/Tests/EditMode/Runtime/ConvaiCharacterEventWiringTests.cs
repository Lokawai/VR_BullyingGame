using System;
using System.Collections.Generic;
using Convai.Application.Services;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Tests.EditMode.Mocks;
using NUnit.Framework;
using UnityEngine;
using ILogger = Convai.Domain.Logging.ILogger;
using Object = UnityEngine.Object;
using TranscriptionPhase = Convai.Domain.Models.TranscriptionPhase;

namespace Convai.Tests.EditMode
{
    /// <summary>
    ///     Tests for ConvaiCharacter event wiring and CharacterReady functionality.
    /// </summary>
    public class ConvaiCharacterEventWiringTests
    {
        private readonly List<GameObject> _createdObjects = new();
        private MockRoomAudioService _audioService;
        private MockRoomConnectionService _connectionService;
        private EventHub _eventHub;
        private MockCharacterLocatorService _locatorService;
        private TestLogger _logger;

        [SetUp]
        public void SetUp()
        {
            _eventHub = new EventHub(new ImmediateScheduler());
            _connectionService = new MockRoomConnectionService();
            _audioService = new MockRoomAudioService();
            _locatorService = new MockCharacterLocatorService();
            _logger = new TestLogger();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _createdObjects)
                if (go != null)
                    Object.DestroyImmediate(go);
            _createdObjects.Clear();
            _eventHub = null;
        }

        private ConvaiCharacter CreateAndInjectCharacter(string characterId = "test-char-id",
            string characterName = "TestCharacter")
        {
            var go = new GameObject(characterName);
            _createdObjects.Add(go);

            var character = go.AddComponent<ConvaiCharacter>();
            character.Configure(characterId, characterName);
            character.Inject(_eventHub, _connectionService, _audioService, _locatorService, _logger);

            return character;
        }

        [Test]
        public void ConvaiCharacter_ImplementsIConvaiCharacterAgent()
        {
            var go = new GameObject("TestCharacter");
            _createdObjects.Add(go);

            var character = go.AddComponent<ConvaiCharacter>();

            Assert.IsTrue(character is IConvaiCharacterAgent,
                "ConvaiCharacter should implement IConvaiCharacterAgent interface");
        }

        [Test]
        public void IsCharacterReady_DefaultsFalse()
        {
            ConvaiCharacter character = CreateAndInjectCharacter();

            Assert.IsFalse(character.IsCharacterReady, "IsCharacterReady should default to false");
        }

        [Test]
        public void OnCharacterReady_IsRaisedWhenCharacterReadyEventPublished()
        {
            ConvaiCharacter character = CreateAndInjectCharacter();
            bool eventRaised = false;
            character.OnCharacterReady += () => eventRaised = true;

            _eventHub.Publish(CharacterReady.Create("test-char-id", "participant-123"));

            Assert.IsTrue(eventRaised, "OnCharacterReady event should be raised");
            Assert.IsTrue(character.IsCharacterReady, "IsCharacterReady should be true after event");
        }

        [Test]
        public void OnCharacterReady_NotRaisedForDifferentCharacterId()
        {
            ConvaiCharacter character = CreateAndInjectCharacter();
            bool eventRaised = false;
            character.OnCharacterReady += () => eventRaised = true;

            _eventHub.Publish(CharacterReady.Create("different-char-id", "participant-456"));

            Assert.IsFalse(eventRaised, "OnCharacterReady should not be raised for different character");
            Assert.IsFalse(character.IsCharacterReady, "IsCharacterReady should remain false");
        }

        [Test]
        public void IsCharacterReady_ResetOnDisconnect()
        {
            ConvaiCharacter character = CreateAndInjectCharacter();

            _eventHub.Publish(CharacterReady.Create("test-char-id", "participant-123"));
            Assert.IsTrue(character.IsCharacterReady, "IsCharacterReady should be true after event");

            _connectionService.RaiseConnectionFailed();

            Assert.IsFalse(character.IsCharacterReady, "IsCharacterReady should be reset on disconnect");
        }

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        private sealed class TestLogger : ILogger
        {
            public List<string> DebugMessages { get; } = new();
            public List<string> WarningMessages { get; } = new();
            public List<string> ErrorMessages { get; } = new();

            public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) { }

            public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Debug(string message, LogCategory category = LogCategory.SDK) => DebugMessages.Add(message);

            public void Debug(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Debug(message, category);

            public void Info(string message, LogCategory category = LogCategory.SDK) { }

            public void Info(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

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

            public bool IsEnabled(LogLevel level, LogCategory category) => true;
        }

        private sealed class TestTranscriptService : IConvaiTranscriptService
        {
            public string LastCharacterId { get; private set; }
            public string LastCharacterName { get; private set; }
            public string LastMessage { get; private set; }
            public bool? LastIsFinal { get; private set; }

            public void BroadcastCharacterMessage(string charID, string charName, string message, bool isLastMessage)
            {
                LastCharacterId = charID;
                LastCharacterName = charName;
                LastMessage = message;
                LastIsFinal = isLastMessage;
            }

            public void BroadcastPlayerMessage(string speakerID, string playerName, string transcript,
                bool finalTranscript, TranscriptionPhase? phase = null)
            {
            }
        }
    }
}
