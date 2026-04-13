using System;
using System.Diagnostics;
using Convai.Application.Services.Transcript;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;
using Convai.Runtime.Presentation.Presenters;
using NUnit.Framework;

namespace Convai.Tests.EditMode
{
    public class TranscriptPresenterTests
    {
        [Test]
        public void Presenter_Forwards_Character_Transcripts_From_EventHub()
        {
            ImmediateScheduler scheduler = new();
            var eventHub = new EventHub(scheduler);

            TranscriptViewModel received = default;
            bool invoked = false;

            using var presenter = new TranscriptPresenter(eventHub);
            {
                presenter.TranscriptReceived += vm =>
                {
                    invoked = true;
                    received = vm;
                };

                var message = TranscriptMessage.Create("npc-1", "NPC", "Hello there", false);
                eventHub.Publish(new CharacterTranscriptReceived(message));
            }

            Assert.IsTrue(invoked, "Presenter should forward character transcripts published via EventHub");
            Assert.AreEqual(TranscriptSpeaker.Character, received.Speaker);
            Assert.AreEqual("NPC", received.DisplayName);
            Assert.AreEqual("npc-1", received.SpeakerId);
            Assert.AreEqual("Hello there", received.Text);
        }

        [Test]
        public void Presenter_Throws_When_EventHub_Is_Null()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                using var presenter = new TranscriptPresenter(null);
            });
        }

        [Test]
        public void TranscriptPresenter_Applies_CustomFormatter()
        {
            ImmediateScheduler scheduler = new();
            var eventHub = new EventHub(scheduler);

            var formatter = new UpperCaseFormatter();
            TranscriptViewModel? captured = null;

            using var presenter = new TranscriptPresenter(eventHub, formatter, new DefaultTranscriptFilter());
            {
                presenter.TranscriptReceived += vm => captured = vm;

                var message = TranscriptMessage.Create("npc-2", "Guide", "Welcome", true);
                eventHub.Publish(new CharacterTranscriptReceived(message));
            }

            Assert.IsTrue(captured.HasValue, "Presenter should forward formatted transcript");
            Assert.AreEqual("WELCOME", captured.Value.FormattedText, "Custom formatter should transform text");
        }

        [Test]
        public void TranscriptPresenter_Respects_Filter()
        {
            var scheduler = new ImmediateScheduler();
            var eventHub = new EventHub(scheduler);

            using var presenter =
                new TranscriptPresenter(eventHub, new DefaultTranscriptFormatter(), new RejectAllFilter());
            bool received = false;
            presenter.TranscriptReceived += _ => received = true;

            var message = TranscriptMessage.Create("npc-3", "Guard", "Halt", false);
            eventHub.Publish(new CharacterTranscriptReceived(message));

            Assert.IsFalse(received, "Filter should prevent transcript from reaching subscribers");
        }

        [Test]
        public void TranscriptPresenter_Publishes_Under_FiveMilliseconds()
        {
            ImmediateScheduler scheduler = new();
            EventHub eventHub = new(scheduler);

            using var presenter = new TranscriptPresenter(eventHub);
            var stopwatch = Stopwatch.StartNew();
            presenter.TranscriptReceived += _ => stopwatch.Stop();

            var message = TranscriptMessage.Create("npc-perf", "Perf NPC", "Timing", true);
            eventHub.Publish(new CharacterTranscriptReceived(message));

            Assert.Less(stopwatch.ElapsedMilliseconds, 5, "Presenter should dispatch transcripts well under 5ms");
        }

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();

            public void ScheduleOnBackground(Action action) => action?.Invoke();

            public bool IsMainThread() => true;
        }

        private sealed class UpperCaseFormatter : ITranscriptFormatter
        {
            public string FormatCharacterMessage(TranscriptMessage message) =>
                message.Text?.ToUpperInvariant() ?? string.Empty;

            public string FormatPlayerMessage(TranscriptMessage message) =>
                message.Text?.ToUpperInvariant() ?? string.Empty;
        }

        private sealed class RejectAllFilter : ITranscriptFilter
        {
            public bool ShouldDisplay(TranscriptMessage message) => false;
        }
    }
}
