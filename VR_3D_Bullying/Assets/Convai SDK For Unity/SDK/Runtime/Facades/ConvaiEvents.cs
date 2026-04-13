using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Narrative;
using Convai.Domain.DomainEvents.Participant;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;

namespace Convai.Runtime.Facades
{
    /// <summary>
    ///     Convenience facade that exposes common Convai domain events as simple C# events.
    ///     Accessed through ConvaiManager.Events.
    /// </summary>
    public sealed class ConvaiEvents : IDisposable
    {
        private SubscriptionToken _actionReceivedToken;
        private SubscriptionToken _characterEmotionToken;
        private SubscriptionToken _characterReadyToken;
        private SubscriptionToken _characterSpeechToken;
        private SubscriptionToken _characterTranscriptToken;
        private SubscriptionToken _characterTurnCompletedToken;
        private bool _isDisposed;
        private SubscriptionToken _micMuteToken;
        private SubscriptionToken _moderationResponseToken;
        private SubscriptionToken _narrativeSectionChangedToken;
        private SubscriptionToken _participantConnectedToken;
        private SubscriptionToken _participantDisconnectedToken;
        private SubscriptionToken _playerSpeakingToken;
        private SubscriptionToken _playerTranscriptToken;
        private SubscriptionToken _sessionErrorToken;

        private SubscriptionToken _sessionStateToken;
        private SubscriptionToken _usageLimitReachedToken;

        internal ConvaiEvents(IEventHub eventHub)
        {
            Raw = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            Subscribe();
        }

        /// <summary>Gets raw EventHub access for advanced scenarios.</summary>
        public IEventHub Raw { get; }

        public void Dispose()
        {
            if (_isDisposed) return;

            Unsubscribe();
            _isDisposed = true;
        }

        /// <summary>Raised whenever session state changes. Parameters: (oldState, newState).</summary>
        public event Action<SessionState, SessionState> OnSessionStateChanged;

        /// <summary>Raised when a room connection succeeds.</summary>
        public event Action OnConnected;

        /// <summary>Raised when a room disconnects.</summary>
        public event Action OnDisconnected;

        /// <summary>Raised when a room enters an error state. Parameter: errorCode.</summary>
        public event Action<string> OnConnectionFailed;

        /// <summary>Raised when character transcript text is received. Parameters: (characterId, text, isFinal).</summary>
        public event Action<string, string, bool> OnCharacterTranscript;

        /// <summary>Raised when player transcript text is received. Parameters: (text, isFinal).</summary>
        public event Action<string, bool> OnPlayerTranscript;

        /// <summary>Raised when character speaking state changes. Parameters: (characterId, isSpeaking).</summary>
        public event Action<string, bool> OnCharacterSpeechStateChanged;

        /// <summary>Raised when character emotion changes. Parameters: (characterId, emotion, intensity).</summary>
        public event Action<string, string, int> OnCharacterEmotionChanged;

        /// <summary>Raised when a character is ready. Parameter: characterId.</summary>
        public event Action<string> OnCharacterReady;

        /// <summary>Raised when character turn completes. Parameters: (characterId, wasInterrupted).</summary>
        public event Action<string, bool> OnCharacterTurnCompleted;

        /// <summary>Raised when player speaking state changes. Parameter: isSpeaking.</summary>
        public event Action<bool> OnPlayerSpeakingStateChanged;

        /// <summary>Raised when local microphone mute state changes. Parameter: isMuted.</summary>
        public event Action<bool> OnMicMuteChanged;

        /// <summary>Raised when participant joins the room.</summary>
        public event Action<ParticipantInfo> OnParticipantJoined;

        /// <summary>Raised when participant leaves the room.</summary>
        public event Action<ParticipantInfo> OnParticipantLeft;

        /// <summary>Raised when narrative section changes. Parameters: (sectionId, characterId).</summary>
        public event Action<string, string> OnNarrativeSectionChanged;

        /// <summary>Raised when a usage quota is exhausted. Parameters: (quotaType, message).</summary>
        public event Action<string, string> OnUsageLimitReached;

        /// <summary>Raised when the backend extracts action tags from a character response. Parameters: (characterId, actions).</summary>
        public event Action<string, IReadOnlyList<string>> OnActionReceived;

        /// <summary>Raised when content moderation evaluates user input. Parameters: (wasFlagged, userInput, reason).</summary>
        public event Action<bool, string, string> OnModerationResponse;

        /// <summary>Raised when the backend pipeline reports an error. Parameters: (errorCode, message, isFatal).</summary>
        public event Action<string, string, bool> OnPipelineError;

        private void Subscribe()
        {
            _sessionStateToken = Raw.Subscribe<SessionStateChanged>(HandleSessionStateChanged);
            _participantConnectedToken = Raw.Subscribe<ParticipantConnected>(HandleParticipantConnected);
            _participantDisconnectedToken = Raw.Subscribe<ParticipantDisconnected>(HandleParticipantDisconnected);
            _characterTranscriptToken = Raw.Subscribe<CharacterTranscriptReceived>(HandleCharacterTranscriptReceived);
            _playerTranscriptToken = Raw.Subscribe<PlayerTranscriptReceived>(HandlePlayerTranscriptReceived);
            _characterSpeechToken = Raw.Subscribe<CharacterSpeechStateChanged>(HandleCharacterSpeechStateChanged);
            _characterEmotionToken = Raw.Subscribe<CharacterEmotionChanged>(HandleCharacterEmotionChanged);
            _characterReadyToken = Raw.Subscribe<CharacterReady>(HandleCharacterReady);
            _characterTurnCompletedToken = Raw.Subscribe<CharacterTurnCompleted>(HandleCharacterTurnCompleted);
            _playerSpeakingToken = Raw.Subscribe<PlayerSpeakingStateChanged>(HandlePlayerSpeakingStateChanged);
            _micMuteToken = Raw.Subscribe<MicMuteChanged>(HandleMicMuteChanged);
            _narrativeSectionChangedToken = Raw.Subscribe<NarrativeSectionChanged>(HandleNarrativeSectionChanged);
            _usageLimitReachedToken = Raw.Subscribe<UsageLimitReached>(HandleUsageLimitReached);
            _actionReceivedToken = Raw.Subscribe<CharacterActionReceived>(HandleActionReceived);
            _moderationResponseToken = Raw.Subscribe<ModerationResponseReceived>(HandleModerationResponse);
            _sessionErrorToken = Raw.Subscribe<SessionError>(HandleSessionError);
        }

        private void Unsubscribe()
        {
            if (_sessionStateToken != default) Raw.Unsubscribe(_sessionStateToken);
            if (_participantConnectedToken != default) Raw.Unsubscribe(_participantConnectedToken);
            if (_participantDisconnectedToken != default) Raw.Unsubscribe(_participantDisconnectedToken);
            if (_characterTranscriptToken != default) Raw.Unsubscribe(_characterTranscriptToken);
            if (_playerTranscriptToken != default) Raw.Unsubscribe(_playerTranscriptToken);
            if (_characterSpeechToken != default) Raw.Unsubscribe(_characterSpeechToken);
            if (_characterEmotionToken != default) Raw.Unsubscribe(_characterEmotionToken);
            if (_characterReadyToken != default) Raw.Unsubscribe(_characterReadyToken);
            if (_characterTurnCompletedToken != default) Raw.Unsubscribe(_characterTurnCompletedToken);
            if (_playerSpeakingToken != default) Raw.Unsubscribe(_playerSpeakingToken);
            if (_micMuteToken != default) Raw.Unsubscribe(_micMuteToken);
            if (_narrativeSectionChangedToken != default) Raw.Unsubscribe(_narrativeSectionChangedToken);
            if (_usageLimitReachedToken != default) Raw.Unsubscribe(_usageLimitReachedToken);
            if (_actionReceivedToken != default) Raw.Unsubscribe(_actionReceivedToken);
            if (_moderationResponseToken != default) Raw.Unsubscribe(_moderationResponseToken);
            if (_sessionErrorToken != default) Raw.Unsubscribe(_sessionErrorToken);

            _sessionStateToken = default;
            _participantConnectedToken = default;
            _participantDisconnectedToken = default;
            _characterTranscriptToken = default;
            _playerTranscriptToken = default;
            _characterSpeechToken = default;
            _characterEmotionToken = default;
            _characterReadyToken = default;
            _characterTurnCompletedToken = default;
            _playerSpeakingToken = default;
            _micMuteToken = default;
            _narrativeSectionChangedToken = default;
            _usageLimitReachedToken = default;
            _actionReceivedToken = default;
            _moderationResponseToken = default;
            _sessionErrorToken = default;
        }

        private void HandleSessionStateChanged(SessionStateChanged e)
        {
            OnSessionStateChanged?.Invoke(e.OldState, e.NewState);

            switch (e.NewState)
            {
                case SessionState.Connected
                    when e.OldState == SessionState.Connecting || e.OldState == SessionState.Reconnecting:
                    OnConnected?.Invoke();
                    break;
                case SessionState.Disconnected when e.OldState != SessionState.Disconnected:
                    OnDisconnected?.Invoke();
                    break;
                case SessionState.Error:
                    OnConnectionFailed?.Invoke(e.ErrorCode);
                    break;
            }
        }

        private void HandleParticipantConnected(ParticipantConnected e) => OnParticipantJoined?.Invoke(e.Participant);

        private void HandleParticipantDisconnected(ParticipantDisconnected e) =>
            OnParticipantLeft?.Invoke(e.Participant);

        private void HandleCharacterTranscriptReceived(CharacterTranscriptReceived e) =>
            OnCharacterTranscript?.Invoke(e.CharacterId, e.Text, e.IsFinal);

        private void HandlePlayerTranscriptReceived(PlayerTranscriptReceived e) =>
            OnPlayerTranscript?.Invoke(e.Text, e.IsFinal);

        private void HandleCharacterSpeechStateChanged(CharacterSpeechStateChanged e) =>
            OnCharacterSpeechStateChanged?.Invoke(e.CharacterId, e.IsSpeaking);

        private void HandleCharacterEmotionChanged(CharacterEmotionChanged e) =>
            OnCharacterEmotionChanged?.Invoke(e.CharacterId, e.Emotion, e.Intensity);

        private void HandleCharacterReady(CharacterReady e) => OnCharacterReady?.Invoke(e.CharacterId);

        private void HandleCharacterTurnCompleted(CharacterTurnCompleted e) =>
            OnCharacterTurnCompleted?.Invoke(e.CharacterId, e.WasInterrupted);

        private void HandlePlayerSpeakingStateChanged(PlayerSpeakingStateChanged e) =>
            OnPlayerSpeakingStateChanged?.Invoke(e.IsSpeaking);

        private void HandleMicMuteChanged(MicMuteChanged e) => OnMicMuteChanged?.Invoke(e.IsMuted);

        private void HandleNarrativeSectionChanged(NarrativeSectionChanged e) =>
            OnNarrativeSectionChanged?.Invoke(e.SectionId, e.CharacterId);

        private void HandleUsageLimitReached(UsageLimitReached e) =>
            OnUsageLimitReached?.Invoke(e.QuotaType, e.Message);

        private void HandleActionReceived(CharacterActionReceived e) =>
            OnActionReceived?.Invoke(e.CharacterId, e.Actions);

        private void HandleModerationResponse(ModerationResponseReceived e) =>
            OnModerationResponse?.Invoke(e.WasFlagged, e.UserInput, e.Reason);

        private void HandleSessionError(SessionError e)
        {
            if (e.IsServerError)
            {
                bool isFatal = e.ErrorCode == SessionErrorCodes.ServerFatalError;
                OnPipelineError?.Invoke(e.ErrorCode, e.Message, isFatal);
            }
        }
    }
}
