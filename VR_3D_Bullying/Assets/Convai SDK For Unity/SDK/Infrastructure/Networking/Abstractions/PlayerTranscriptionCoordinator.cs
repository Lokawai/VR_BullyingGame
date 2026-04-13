using System;
using System.Diagnostics;
using Convai.Domain.Logging;
using Convai.Domain.Models;

namespace Convai.Infrastructure.Networking
{
    /// <summary>
    ///     Coordinates player transcription state transitions for a single speaking session.
    ///     Manages the lifecycle from start → interim → final → completion phases.
    ///     Supports multi-user speaker attribution via <see cref="SpeakerInfo" />.
    /// </summary>
    /// <remarks>
    ///     This class is a Transport-layer component that tracks transcription phases
    ///     and dispatches events to <see cref="IConvaiPlayerEvents" />. It does NOT
    ///     select "best text" - that's Application layer responsibility.
    ///     Thread-safety: All public methods should be called from the same thread.
    ///     Events are dispatched via the provided scheduler to ensure main thread execution.
    /// </remarks>
    internal sealed class PlayerTranscriptionCoordinator
    {
        private readonly ILogger _logger;
        private readonly IConvaiPlayerEvents _playerEvents;
        private readonly Action<Action> _scheduleOnMainThread;
        private string _asrFinalText = string.Empty;
        private bool _awaitingProcessedFinal;
        private bool _completionDispatched;
        private string _processedFinalText = string.Empty;
        private bool _receivedAsrFinal;
        private bool _receivedProcessedFinal;
        private bool _sessionActive;

        private string _sessionId = string.Empty;

        // Multi-user speaker attribution from ProcessedFinal
        private bool _stopPending;

        /// <summary>
        ///     Creates a new transcription coordinator.
        /// </summary>
        /// <param name="playerEvents">Event interface for broadcasting transcription events.</param>
        /// <param name="scheduleOnMainThread">Scheduler for dispatching events on the main thread.</param>
        /// <param name="logger">Optional logger for diagnostic output.</param>
        /// <exception cref="ArgumentNullException">Thrown if required parameters are null.</exception>
        public PlayerTranscriptionCoordinator(IConvaiPlayerEvents playerEvents, Action<Action> scheduleOnMainThread,
            ILogger logger = null)
        {
            _playerEvents = playerEvents ?? throw new ArgumentNullException(nameof(playerEvents));
            _scheduleOnMainThread =
                scheduleOnMainThread ?? throw new ArgumentNullException(nameof(scheduleOnMainThread));
            _logger = logger;
        }

        /// <summary>
        ///     Gets the current speaker info for the session.
        /// </summary>
        public SpeakerInfo CurrentSpeakerInfo { get; private set; } = SpeakerInfo.Empty;

        /// <summary>
        ///     Handles the start of a speaking session.
        /// </summary>
        public void HandleStart()
        {
            if (_sessionActive && !_stopPending) return;

            if (_stopPending && !_completionDispatched) CompleteSession();

            StartNewSession();
        }

        /// <summary>
        ///     Handles an interim transcription update.
        /// </summary>
        /// <param name="interimText">Interim transcript text.</param>
        public void HandleInterim(string interimText)
        {
            EnsureSession();

            string safeText = interimText ?? string.Empty;

            // DIAGNOSTIC: Trace interim transcript flow
            _logger?.Debug($"[PlayerTranscriptionCoordinator] Dispatching interim phase, text=\"{safeText}\"",
                LogCategory.Player);

            Dispatch(() => _playerEvents.OnPlayerTranscriptionReceived(safeText, TranscriptionPhase.Interim));
        }

        /// <summary>
        ///     Handles an ASR-final transcription update.
        /// </summary>
        /// <param name="finalText">Final transcript text emitted by the ASR engine.</param>
        public void HandleAsrFinal(string finalText)
        {
            EnsureSession();

            _asrFinalText = finalText ?? string.Empty;
            _receivedAsrFinal = _asrFinalText.Length > 0;

            Dispatch(() => _playerEvents.OnPlayerTranscriptionReceived(_asrFinalText, TranscriptionPhase.AsrFinal));

            if (_stopPending && !_awaitingProcessedFinal) CompleteSession();
        }

        /// <summary>
        ///     Handles a processed-final transcription update with speaker attribution.
        /// </summary>
        /// <param name="cleanedText">Cleaned final transcript text.</param>
        /// <param name="speakerInfo">Speaker attribution data from the backend.</param>
        public void HandleProcessedFinal(string cleanedText, SpeakerInfo speakerInfo)
        {
            EnsureSession();

            _processedFinalText = cleanedText ?? string.Empty;
            _receivedProcessedFinal = _processedFinalText.Length > 0;
            CurrentSpeakerInfo = speakerInfo;

            Dispatch(() =>
                _playerEvents.OnPlayerTranscriptionReceived(_processedFinalText, TranscriptionPhase.ProcessedFinal,
                    speakerInfo));

            if (_stopPending)
            {
                _awaitingProcessedFinal = false;
                CompleteSession();
            }
        }

        /// <summary>
        ///     Handles a processed-final transcription update without speaker attribution.
        /// </summary>
        /// <param name="cleanedText">Cleaned final transcript text.</param>
        public void HandleProcessedFinal(string cleanedText) => HandleProcessedFinal(cleanedText, SpeakerInfo.Empty);

        /// <summary>
        ///     Handles the stop/end of a speaking session.
        /// </summary>
        public void HandleStop()
        {
            if (string.IsNullOrEmpty(_sessionId)) return;

            _stopPending = true;
            _sessionActive = false;
            _awaitingProcessedFinal = !_receivedProcessedFinal;

            if (!_awaitingProcessedFinal) CompleteSession();
        }

        /// <summary>
        ///     Resets internal state and clears any in-flight session tracking.
        /// </summary>
        public void Reset()
        {
            _sessionId = string.Empty;
            _sessionActive = false;
            _receivedAsrFinal = false;
            _receivedProcessedFinal = false;
            _stopPending = false;
            _awaitingProcessedFinal = false;
            _completionDispatched = false;
            _asrFinalText = string.Empty;
            _processedFinalText = string.Empty;
            CurrentSpeakerInfo = SpeakerInfo.Empty;
        }

        private void StartNewSession()
        {
            Reset();

            _sessionId = Guid.NewGuid().ToString("N");
            _sessionActive = true;

            Dispatch(() => _playerEvents.OnPlayerStartedSpeaking(_sessionId));
            Dispatch(() => _playerEvents.OnPlayerTranscriptionReceived(string.Empty, TranscriptionPhase.Listening));
        }

        private void EnsureSession()
        {
            if (string.IsNullOrEmpty(_sessionId)) StartNewSession();
        }

        private void CompleteSession()
        {
            if (_completionDispatched || string.IsNullOrEmpty(_sessionId))
            {
                Reset();
                return;
            }

            // ARCHITECTURE: Do NOT select "best text" here - that's Application layer logic.
            // Forward the Completed phase with empty text; the Application layer will use
            // the buffered AsrFinal/ProcessedFinal texts to determine the final transcript.
            // This ensures the Transport layer remains a "dumb" pass-through.
            bool producedFinal = _receivedAsrFinal || _receivedProcessedFinal;

            // Capture speaker info before reset
            SpeakerInfo speakerInfo = CurrentSpeakerInfo;

            Dispatch(() =>
                _playerEvents.OnPlayerTranscriptionReceived(string.Empty, TranscriptionPhase.Completed, speakerInfo));
            Dispatch(() => _playerEvents.OnPlayerStoppedSpeaking(_sessionId, producedFinal));

            _completionDispatched = true;
            Reset();
        }

        private void Dispatch(Action action)
        {
            void SafeInvoke()
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    // Log but do not rethrow — exceptions in player callbacks must not crash the transport layer.
                    Debug.WriteLine($"[PlayerTranscriptionCoordinator] Exception in dispatch callback: {ex}");
                }
            }

            _scheduleOnMainThread(SafeInvoke);
        }
    }
}
