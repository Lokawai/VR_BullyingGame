using System;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Logging;
using TranscriptionPhase = Convai.Domain.Models.TranscriptionPhase;

namespace Convai.Runtime.Services.Transcript
{
    /// <summary>
    ///     Adapter that bridges player ASR events to the domain transcript pipeline.
    ///     Implements <see cref="IConvaiPlayerEvents" /> to receive transcription phases
    ///     and publishes <see cref="PlayerTranscriptReceived" /> domain events via EventHub.
    ///     Supports multi-user speaker attribution via <see cref="SpeakerInfo" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Architecture:</b> This adapter is a "dumb" pass-through component. It forwards
    ///         ALL transcription phases to the Application layer without any filtering or processing.
    ///         The Application layer (TranscriptPresenter) is responsible for deciding which phases
    ///         to display and how to aggregate them.
    ///     </para>
    ///     <para>
    ///         <b>Phase Handling:</b> Incoming transcription phases are already the canonical
    ///         domain <see cref="TranscriptionPhase" /> values and are forwarded as-is.
    ///     </para>
    ///     <para>
    ///         <b>Multi-User Support:</b> When <see cref="SpeakerInfo" /> is provided via the overloaded
    ///         callback, it is passed through to the domain event for speaker attribution in multi-user
    ///         scenarios.
    ///     </para>
    /// </remarks>
    public sealed class PlayerTranscriptAdapter : IConvaiPlayerEvents, IDisposable
    {
        private readonly string _defaultPlayerName;
        private readonly IEventHub _eventHub;
        private readonly Func<string> _playerNameProvider;
        private bool _isDisposed;

        private SpeakerInfo _lastSpeakerInfo = SpeakerInfo.Empty;

        /// <summary>
        ///     Creates a new PlayerTranscriptAdapter.
        /// </summary>
        /// <param name="eventHub">Event hub for publishing domain events. Required.</param>
        /// <param name="playerId">Unique identifier for the player. Required.</param>
        /// <param name="playerName">Default display name for the player. Defaults to "You" if null or empty.</param>
        /// <param name="playerNameProvider">Optional dynamic player-name provider used at publish time.</param>
        /// <exception cref="ArgumentNullException">Thrown if eventHub is null.</exception>
        /// <exception cref="ArgumentException">Thrown if playerId is null or empty.</exception>
        public PlayerTranscriptAdapter(
            IEventHub eventHub,
            string playerId,
            string playerName = null,
            Func<string> playerNameProvider = null)
        {
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));

            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("Player ID cannot be null or empty.", nameof(playerId));

            PlayerId = playerId;
            _defaultPlayerName = string.IsNullOrWhiteSpace(playerName) ? "You" : playerName;
            _playerNameProvider = playerNameProvider;
        }

        /// <summary>
        ///     Gets the player ID this adapter is associated with.
        /// </summary>
        public string PlayerId { get; }

        /// <summary>
        ///     Gets the player display name.
        /// </summary>
        public string PlayerName => ResolvePlayerName();

        /// <inheritdoc />
        public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase) =>
            OnPlayerTranscriptionReceived(transcript, transcriptionPhase, SpeakerInfo.Empty);

        /// <inheritdoc />
        public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase,
            SpeakerInfo speakerInfo)
        {
            if (_isDisposed)
                return;

            ConvaiLogger.Debug(
                $"[PlayerTranscriptAdapter] Transcription received: phase={transcriptionPhase}, text=\"{transcript}\"",
                LogCategory.Player);

            if (speakerInfo.IsValid) _lastSpeakerInfo = speakerInfo;

            SpeakerInfo effectiveSpeakerInfo = speakerInfo.IsValid ? speakerInfo : _lastSpeakerInfo;

            string fallbackPlayerName = ResolvePlayerName();
            string speakerId = effectiveSpeakerInfo.IsValid ? effectiveSpeakerInfo.SpeakerId : PlayerId;
            string speakerName = effectiveSpeakerInfo.IsValid ? effectiveSpeakerInfo.SpeakerName : fallbackPlayerName;

            string safeText = transcript ?? string.Empty;

            var domainEvent = PlayerTranscriptReceived.Create(
                speakerId,
                string.IsNullOrEmpty(speakerName) ? fallbackPlayerName : speakerName,
                safeText,
                false,
                transcriptionPhase,
                speakerInfo: effectiveSpeakerInfo
            );

            _eventHub.Publish(domainEvent);

            if (transcriptionPhase == TranscriptionPhase.Completed) _lastSpeakerInfo = SpeakerInfo.Empty;
        }

        /// <inheritdoc />
        public void OnPlayerStartedSpeaking(string sessionId)
        {
        }

        /// <inheritdoc />
        public void OnPlayerStoppedSpeaking(string sessionId, bool didProduceFinalTranscript)
        {
        }

        /// <inheritdoc />
        public void Dispose() => _isDisposed = true;

        private string ResolvePlayerName()
        {
            string runtimeName = _playerNameProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(runtimeName)) return _defaultPlayerName;

            return runtimeName.Trim();
        }
    }
}
