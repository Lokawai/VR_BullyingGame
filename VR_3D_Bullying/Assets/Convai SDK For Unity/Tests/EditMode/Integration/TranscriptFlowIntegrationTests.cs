using System;
using System.Collections.Generic;
using Convai.Application.Services;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using NUnit.Framework;

namespace Convai.Tests.EditMode
{
    /// <summary>
    ///     Integration tests for transcript flow when no behavior components intercept messages.
    ///     Verifies that ConvaiTranscriptService broadcasts messages correctly via EventHub.
    /// </summary>
    [Category("Integration")]
    public class TranscriptFlowIntegrationTests
    {
        private EventHub _eventHub;
        private TestLogger _logger;
        private ConvaiTranscriptService _transcriptService;

        [SetUp]
        public void SetUp()
        {
            _logger = new TestLogger();
            _eventHub = new EventHub(new ImmediateScheduler(), _logger);
            _transcriptService = new ConvaiTranscriptService(_eventHub, _logger);
        }

        [TearDown]
        public void TearDown()
        {
            _eventHub = null;
            _transcriptService = null;
        }

        [Test]
        public void NPC_BroadcastsTranscript_WhenNoBehaviorExists()
        {
            CharacterTranscriptReceived? receivedEvent = null;
            SubscriptionToken token = _eventHub.Subscribe<CharacterTranscriptReceived>(e =>
            {
                receivedEvent = e;
            });

            _transcriptService.BroadcastCharacterMessage(
                "npc-123",
                "TestNPC",
                "Hello, I am an NPC!",
                true
            );

            Assert.IsNotNull(receivedEvent, "CharacterTranscriptReceived event should be published");
            Assert.AreEqual("npc-123", receivedEvent.Value.Message.SpeakerId, "Character ID should match");
            Assert.AreEqual("TestNPC", receivedEvent.Value.Message.DisplayName, "Character name should match");
            Assert.AreEqual("Hello, I am an NPC!", receivedEvent.Value.Message.Text, "Message text should match");
            Assert.IsTrue(receivedEvent.Value.Message.IsFinal, "Message should be marked as final");

            _eventHub.Unsubscribe(token);
        }

        [Test]
        public void Player_BroadcastsTranscript_WhenNoBehaviorExists()
        {
            PlayerTranscriptReceived? receivedEvent = null;
            SubscriptionToken token = _eventHub.Subscribe<PlayerTranscriptReceived>(e =>
            {
                receivedEvent = e;
            });

            _transcriptService.BroadcastPlayerMessage(
                "player-456",
                "TestPlayer",
                "Hello, I am the player!",
                true,
                TranscriptionPhase.Completed
            );

            Assert.IsNotNull(receivedEvent, "PlayerTranscriptReceived event should be published");
            Assert.AreEqual("player-456", receivedEvent.Value.Message.SpeakerId, "Player ID should match");
            Assert.AreEqual("TestPlayer", receivedEvent.Value.Message.DisplayName, "Player name should match");
            Assert.AreEqual("Hello, I am the player!", receivedEvent.Value.Message.Text,
                "Transcript text should match");
            Assert.IsTrue(receivedEvent.Value.Message.IsFinal, "Transcript should be marked as final");
            Assert.AreEqual(TranscriptionPhase.Completed, receivedEvent.Value.Phase, "Phase should be Completed");

            _eventHub.Unsubscribe(token);
        }

        [Test]
        public void NPC_BroadcastsInterimTranscript_WithCorrectFinality()
        {
            List<CharacterTranscriptReceived> receivedEvents = new();
            SubscriptionToken token = _eventHub.Subscribe<CharacterTranscriptReceived>(e =>
            {
                receivedEvents.Add(e);
            });

            _transcriptService.BroadcastCharacterMessage("npc-123", "TestNPC", "Hello...", false);
            _transcriptService.BroadcastCharacterMessage("npc-123", "TestNPC", "Hello, world!", true);

            Assert.AreEqual(2, receivedEvents.Count, "Should receive 2 transcript events");
            Assert.IsFalse(receivedEvents[0].Message.IsFinal, "First message should be interim");
            Assert.IsTrue(receivedEvents[1].Message.IsFinal, "Second message should be final");

            _eventHub.Unsubscribe(token);
        }

        [Test]
        public void Player_BroadcastsInterimTranscript_DerivesPhasefromFinality()
        {
            List<PlayerTranscriptReceived> receivedEvents = new();
            SubscriptionToken token = _eventHub.Subscribe<PlayerTranscriptReceived>(e =>
            {
                receivedEvents.Add(e);
            });

            _transcriptService.BroadcastPlayerMessage("player-1", "Player", "Speaking...", false);
            _transcriptService.BroadcastPlayerMessage("player-1", "Player", "Done speaking.", true);

            Assert.AreEqual(2, receivedEvents.Count, "Should receive 2 transcript events");
            Assert.AreEqual(TranscriptionPhase.Interim, receivedEvents[0].Phase, "Non-final should have Interim phase");
            Assert.AreEqual(TranscriptionPhase.Completed, receivedEvents[1].Phase, "Final should have Completed phase");

            _eventHub.Unsubscribe(token);
        }

        #region Test Helpers

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        private sealed class TestLogger : ILogger
        {
            public List<string> DebugMessages { get; } = new();
            public List<string> InfoMessages { get; } = new();
            public List<string> ErrorMessages { get; } = new();

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

            public void Warning(string message, LogCategory category = LogCategory.SDK) { }

            public void Warning(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Error(string message, LogCategory category = LogCategory.SDK) => ErrorMessages.Add(message);

            public void Error(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Error(message, category);

            public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK) =>
                ErrorMessages.Add(message ?? exception.Message);

            public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK) => Error(exception, message, category);

            public bool IsEnabled(LogLevel level, LogCategory category) => true;
        }

        #endregion
    }
}
