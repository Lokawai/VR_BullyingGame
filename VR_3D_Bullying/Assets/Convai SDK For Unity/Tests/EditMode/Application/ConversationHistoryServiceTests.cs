using System;
using System.Collections.Generic;
using Convai.Application.Services.Transcript;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Application
{
    /// <summary>
    ///     Unit tests for <see cref="ConversationHistoryService" />.
    /// </summary>
    public class ConversationHistoryServiceTests
    {
        [Test]
        public void Stores_Final_Character_Transcripts()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub);

            TranscriptMessage message = TranscriptMessage.ForCharacter(
                "char-1",
                "Alice",
                "Hello!",
                true
            );
            hub.Publish(new CharacterTranscriptReceived(message));

            Assert.AreEqual(1, history.Count);
            Assert.AreEqual("Hello!", history.Entries[0].Message.Text);
            Assert.AreEqual(SpeakerType.Character, history.Entries[0].Speaker);
        }

        [Test]
        public void Ignores_Interim_Character_Transcripts()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub);

            TranscriptMessage message = TranscriptMessage.ForCharacter(
                "char-1",
                "Alice",
                "Hello...",
                false
            );
            hub.Publish(new CharacterTranscriptReceived(message));

            Assert.AreEqual(0, history.Count);
        }

        [Test]
        public void Stores_ProcessedFinal_Player_Transcripts()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub);

            var evt = PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Hi there!",
                true,
                TranscriptionPhase.ProcessedFinal
            );
            hub.Publish(evt);

            Assert.AreEqual(1, history.Count);
            Assert.AreEqual("Hi there!", history.Entries[0].Message.Text);
            Assert.AreEqual(SpeakerType.Player, history.Entries[0].Speaker);
        }

        [Test]
        public void Ignores_Interim_Player_Transcripts()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub);

            var evt = PlayerTranscriptReceived.Create(
                "player-1",
                "You",
                "Hi...",
                false
            );
            hub.Publish(evt);

            Assert.AreEqual(0, history.Count);
        }

        [Test]
        public void Respects_MaxEntries_Limit()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub, 3);

            for (int i = 1; i <= 5; i++)
            {
                TranscriptMessage message = TranscriptMessage.ForCharacter(
                    "char-1",
                    "Alice",
                    $"Message {i}",
                    true
                );
                hub.Publish(new CharacterTranscriptReceived(message));
            }

            Assert.AreEqual(3, history.Count);
            Assert.AreEqual("Message 3", history.Entries[0].Message.Text);
            Assert.AreEqual("Message 4", history.Entries[1].Message.Text);
            Assert.AreEqual("Message 5", history.Entries[2].Message.Text);
        }

        [Test]
        public void Clear_Removes_All_Entries()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub);

            TranscriptMessage message = TranscriptMessage.ForCharacter(
                "char-1",
                "Alice",
                "Hello!",
                true
            );
            hub.Publish(new CharacterTranscriptReceived(message));

            history.Clear();

            Assert.AreEqual(0, history.Count);
        }

        [Test]
        public void GetEntriesBySpeaker_Filters_Correctly()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub);

            hub.Publish(new CharacterTranscriptReceived(
                TranscriptMessage.ForCharacter("char-1", "Alice", "Hello!", true)));
            hub.Publish(new CharacterTranscriptReceived(
                TranscriptMessage.ForCharacter("char-2", "Bob", "Hi!", true)));
            hub.Publish(new CharacterTranscriptReceived(
                TranscriptMessage.ForCharacter("char-1", "Alice", "How are you?", true)));

            IReadOnlyList<TranscriptEntry> aliceEntries = history.GetEntriesBySpeaker("char-1");

            Assert.AreEqual(2, aliceEntries.Count);
            Assert.AreEqual("Hello!", aliceEntries[0].Message.Text);
            Assert.AreEqual("How are you?", aliceEntries[1].Message.Text);
        }

        [Test]
        public void Export_PlainText_Format()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub);

            hub.Publish(new CharacterTranscriptReceived(
                TranscriptMessage.ForCharacter("char-1", "Alice", "Hello!", true)));

            string exported = history.Export(ConversationExportFormat.PlainText);

            Assert.IsTrue(exported.Contains("Alice"));
            Assert.IsTrue(exported.Contains("Hello!"));
        }

        [Test]
        public void Export_Json_Format()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub);

            hub.Publish(new CharacterTranscriptReceived(
                TranscriptMessage.ForCharacter("char-1", "Alice", "Hello!", true)));

            string exported = history.Export(ConversationExportFormat.Json);

            Assert.IsTrue(exported.StartsWith("["));
            Assert.IsTrue(exported.EndsWith("]"));
            Assert.IsTrue(exported.Contains("\"speaker\":\"Alice\""));
            Assert.IsTrue(exported.Contains("\"text\":\"Hello!\""));
        }

        [Test]
        public void Export_Markdown_Format()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub);

            hub.Publish(new CharacterTranscriptReceived(
                TranscriptMessage.ForCharacter("char-1", "Alice", "Hello!", true)));

            string exported = history.Export(ConversationExportFormat.Markdown);

            Assert.IsTrue(exported.Contains("# Conversation Transcript"));
            Assert.IsTrue(exported.Contains("**Alice**"));
            Assert.IsTrue(exported.Contains("> Hello!"));
        }

        [Test]
        public void EntryAdded_Event_Fires()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub);

            TranscriptEntry capturedEntry = null;
            history.EntryAdded += entry => capturedEntry = entry;

            hub.Publish(new CharacterTranscriptReceived(
                TranscriptMessage.ForCharacter("char-1", "Alice", "Hello!", true)));

            Assert.IsNotNull(capturedEntry);
            Assert.AreEqual("Hello!", capturedEntry.Message.Text);
        }

        [Test]
        public void Ignores_Empty_Text()
        {
            var hub = new TestEventHub();
            using var history = new ConversationHistoryService(hub);

            hub.Publish(new CharacterTranscriptReceived(
                TranscriptMessage.ForCharacter("char-1", "Alice", "", true)));
            hub.Publish(new CharacterTranscriptReceived(
                TranscriptMessage.ForCharacter("char-1", "Alice", "   ", true)));

            Assert.AreEqual(0, history.Count);
        }

        private sealed class TestEventHub : IEventHub
        {
            private readonly List<IEventSubscriber<CharacterTranscriptReceived>> _characterSubs = new();
            private readonly List<IEventSubscriber<PlayerTranscriptReceived>> _playerSubs = new();

            public void Publish<TEvent>(TEvent @event) where TEvent : notnull
            {
                if (@event is CharacterTranscriptReceived ctr)
                {
                    foreach (IEventSubscriber<CharacterTranscriptReceived> sub in _characterSubs)
                        sub.OnEvent(ctr);
                }
                else if (@event is PlayerTranscriptReceived ptr)
                {
                    foreach (IEventSubscriber<PlayerTranscriptReceived> sub in _playerSubs)
                        sub.OnEvent(ptr);
                }
            }

            public SubscriptionToken Subscribe<TEvent>(IEventSubscriber<TEvent> subscriber,
                EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
            {
                // Use typeof(TEvent) to correctly route subscriptions when subscriber implements multiple interfaces
                if (typeof(TEvent) == typeof(CharacterTranscriptReceived))
                    _characterSubs.Add((IEventSubscriber<CharacterTranscriptReceived>)subscriber);
                else if (typeof(TEvent) == typeof(PlayerTranscriptReceived))
                    _playerSubs.Add((IEventSubscriber<PlayerTranscriptReceived>)subscriber);
                return SubscriptionToken.Create();
            }

            public SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler,
                EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
                => SubscriptionToken.Create();

            public SubscriptionToken Subscribe<TEvent>(IEventSubscriber<TEvent> subscriber, IEventFilter<TEvent> filter,
                EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
                => SubscriptionToken.Create();

            public SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler, IEventFilter<TEvent> filter,
                EventDeliveryPolicy deliveryPolicy = EventDeliveryPolicy.MainThread) where TEvent : notnull
                => SubscriptionToken.Create();

            public void Unsubscribe(SubscriptionToken token) { }

            public bool TryPeriodicCleanup(float currentTimeSeconds, float cleanupIntervalSeconds = 60f) => false;
        }
    }
}
