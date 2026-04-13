using System;
using Convai.Application.Services.Transcript;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Logging;

namespace Convai.Runtime.Presentation.Presenters
{
    /// <summary>
    ///     Bridges domain transcript events to UI view-models.
    ///     Subscribes to <see cref="CharacterTranscriptReceived" /> and <see cref="PlayerTranscriptReceived" />
    ///     domain events and transforms them into <see cref="TranscriptViewModel" /> for UI consumption.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Architecture:</b> This presenter is a thin adapter that delegates business logic
    ///         to Application-layer services:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>
    ///                     <see cref="IPlayerTranscriptAggregator" />: Handles phase buffering and final text
    ///                     selection
    ///                 </description>
    ///             </item>
    ///             <item>
    ///                 <description><see cref="ITranscriptFilter" />: Determines which messages to display</description>
    ///             </item>
    ///             <item>
    ///                 <description><see cref="ITranscriptFormatter" />: Formats messages for presentation</description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Phase Processing:</b>
    ///         <list type="bullet">
    ///             <item>
    ///                 <description><c>Idle</c>, <c>Listening</c>: Suppressed (no display value)</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>Interim</c>: Displayed as live transcription (IsFinal=false)</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>AsrFinal</c>: Buffered via aggregator for final text selection</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>ProcessedFinal</c>: Buffered via aggregator (preferred over AsrFinal)</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>Completed</c>: Emits final transcript using aggregator's best available text</description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Event Flow:</b>
    ///         <code>
    /// Transport → Domain Events → TranscriptPresenter (routing) → Application Services → TranscriptViewModel → UI
    /// </code>
    ///     </para>
    /// </remarks>
    public sealed class TranscriptPresenter : IDisposable,
        IEventSubscriber<CharacterTranscriptReceived>,
        IEventSubscriber<PlayerTranscriptReceived>
    {
        private readonly IPlayerTranscriptAggregator _aggregator;
        private readonly SubscriptionToken _characterToken;
        private readonly IEventHub _eventHub;
        private readonly ITranscriptFilter _filter;
        private readonly ITranscriptFormatter _formatter;
        private readonly ILogger _logger;
        private readonly SubscriptionToken _playerToken;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TranscriptPresenter" /> class.
        /// </summary>
        /// <param name="eventHub">Event hub used to subscribe to transcript domain events.</param>
        /// <param name="formatter">Optional transcript formatter.</param>
        /// <param name="filter">Optional transcript filter.</param>
        /// <param name="aggregator">Optional player transcript aggregator.</param>
        /// <param name="logger">Optional logger.</param>
        public TranscriptPresenter(
            IEventHub eventHub,
            ITranscriptFormatter formatter = null,
            ITranscriptFilter filter = null,
            IPlayerTranscriptAggregator aggregator = null,
            ILogger logger = null)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _formatter = formatter ?? new DefaultTranscriptFormatter();
            _filter = filter ?? new DefaultTranscriptFilter();
            _aggregator = aggregator ?? new PlayerTranscriptAggregator();
            _logger = logger;

            _characterToken = _eventHub.Subscribe<CharacterTranscriptReceived>(this);
            _playerToken = _eventHub.Subscribe<PlayerTranscriptReceived>(this);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _eventHub.Unsubscribe(_characterToken);
            _eventHub.Unsubscribe(_playerToken);
        }

        /// <inheritdoc />
        public void OnEvent(CharacterTranscriptReceived e)
        {
            TranscriptMessage message = e.Message;

            if (!_filter.ShouldDisplay(message)) return;

            string formattedText = _formatter.FormatCharacterMessage(message);
            TranscriptViewModel viewModel = new(TranscriptSpeaker.Character, message, formattedText);
            if (!viewModel.IsEmpty) TranscriptReceived(viewModel);
        }

        /// <inheritdoc />
        public void OnEvent(PlayerTranscriptReceived e)
        {
            TranscriptMessage message = e.Message;
            TranscriptionPhase phase = e.Phase;
            SpeakerInfo speakerInfo = e.SpeakerInfo;

            ConvaiLogger.Debug(
                $"[TranscriptPresenter] Player transcript received: phase={phase}, text=\"{message.Text}\"",
                LogCategory.Player);

            _logger?.Debug(
                $"[TranscriptPresenter] PlayerTranscriptReceived: Phase={phase}, SpeakerId='{message.SpeakerId}', Text='{message.Text}', HasSpeakerInfo={speakerInfo.IsValid}");

            _aggregator.UpdateSpeakerInfo(speakerInfo);

            switch (phase)
            {
                case TranscriptionPhase.Idle:
                case TranscriptionPhase.Listening:
                    _logger?.Debug($"[TranscriptPresenter] Suppressing {phase} phase");
                    return;

                case TranscriptionPhase.Interim:
                    HandlePlayerInterim(message);
                    break;

                case TranscriptionPhase.AsrFinal:
                    HandlePlayerAsrFinal(message);
                    break;

                case TranscriptionPhase.ProcessedFinal:
                    HandlePlayerProcessedFinal(message);
                    break;

                case TranscriptionPhase.Completed:
                    HandlePlayerCompleted(message);
                    break;

                default:
                    _logger?.Debug($"[TranscriptPresenter] Unknown phase {phase}, suppressing");
                    return;
            }
        }

        /// <inheritdoc />
        public event Action<TranscriptViewModel> TranscriptReceived = delegate { };

        /// <summary>
        ///     Handles interim player transcripts - display as live transcription.
        /// </summary>
        private void HandlePlayerInterim(TranscriptMessage message)
        {
            ConvaiLogger.Debug($"[TranscriptPresenter] HandlePlayerInterim entry: text=\"{message.Text}\"",
                LogCategory.Player);

            if (string.IsNullOrWhiteSpace(message.Text))
            {
                ConvaiLogger.Debug("[TranscriptPresenter] HandlePlayerInterim skipped: empty text", LogCategory.Player);
                return;
            }

            if (!_filter.ShouldDisplay(message))
            {
                ConvaiLogger.Debug("[TranscriptPresenter] HandlePlayerInterim filtered out by filter",
                    LogCategory.Player);
                _logger?.Debug("[TranscriptPresenter] Player interim filtered out");
                return;
            }

            string formattedText = _formatter.FormatPlayerMessage(message);
            TranscriptViewModel viewModel = new(TranscriptSpeaker.Player, message, formattedText);

            ConvaiLogger.Debug($"[TranscriptPresenter] HandlePlayerInterim dispatching to UI, text=\"{message.Text}\"",
                LogCategory.Player);
            _logger?.Debug($"[TranscriptPresenter] Dispatching player interim: '{message.Text}'");
            TranscriptReceived(viewModel);
        }

        /// <summary>
        ///     Handles ASR final - buffer for potential use in final text via aggregator.
        /// </summary>
        private void HandlePlayerAsrFinal(TranscriptMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Text)) return;

            _aggregator.AppendAsrFinal(message.Text, message.SpeakerId, message.DisplayName);
            _logger?.Debug(
                $"[TranscriptPresenter] Buffered ASR final: '{message.Text}', cumulative: '{_aggregator.AsrFinalText}'");
        }

        /// <summary>
        ///     Handles processed final - buffer for final text via aggregator (preferred over ASR final).
        ///     Also dispatches to UI for live display.
        /// </summary>
        private void HandlePlayerProcessedFinal(TranscriptMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Text)) return;

            _aggregator.SetProcessedFinal(message.Text, message.SpeakerId, message.DisplayName, message.ParticipantId);
            _logger?.Debug($"[TranscriptPresenter] Buffered processed final: '{message.Text}'");

            if (!_filter.ShouldDisplay(message))
            {
                _logger?.Debug("[TranscriptPresenter] ProcessedFinal filtered out");
                return;
            }

            string formattedText = _formatter.FormatPlayerMessage(message);
            TranscriptViewModel viewModel = new(TranscriptSpeaker.Player, message, formattedText);

            _logger?.Debug($"[TranscriptPresenter] Dispatching processed final: '{message.Text}'");
            TranscriptReceived(viewModel);
        }

        /// <summary>
        ///     Handles completed phase - emit final transcript using aggregator's best available text.
        /// </summary>
        private void HandlePlayerCompleted(TranscriptMessage message)
        {
            _logger?.Debug(
                $"[TranscriptPresenter] Completing player turn. ProcessedFinal='{_aggregator.ProcessedFinalText}', AsrFinal='{_aggregator.AsrFinalText}'");

            PlayerTranscriptResult result = _aggregator.Complete();

            if (!result.HasText)
            {
                _logger?.Debug("[TranscriptPresenter] No final text available, skipping completion");
                return;
            }

            string playerId = result.PlayerId ?? message.SpeakerId;
            string displayName = result.DisplayName ?? message.DisplayName;
            string participantId = result.ParticipantId ?? message.ParticipantId;

            TranscriptMessage finalMessage = TranscriptMessage.ForPlayer(
                result.FinalText,
                true,
                playerId,
                displayName,
                participantId
            );

            if (!_filter.ShouldDisplay(finalMessage))
            {
                _logger?.Debug("[TranscriptPresenter] Final player transcript filtered out");
                return;
            }

            string formattedText = _formatter.FormatPlayerMessage(finalMessage);
            TranscriptViewModel viewModel = new(TranscriptSpeaker.Player, finalMessage, formattedText);

            _logger?.Debug(
                $"[TranscriptPresenter] Dispatching final player transcript: '{result.FinalText}' (speaker: {displayName})");
            TranscriptReceived(viewModel);
        }
    }
}
