using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Presenters;

namespace Convai.Runtime.Presentation.Strategies
{
    /// <summary>
    ///     Chat-style presentation strategy that aggregates multiple transcript chunks
    ///     from the same speaker into a single message bubble.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Aggregation Logic:</b>
    ///         <list type="bullet">
    ///             <item>
    ///                 <description><b>Character messages:</b> Incremental append - each chunk adds to the message</description>
    ///             </item>
    ///             <item>
    ///                 <description><b>Player ASR:</b> Cumulative replacement - each interim replaces previous text</description>
    ///             </item>
    ///             <item>
    ///                 <description><b>Speaker change:</b> Completes previous character message when new character speaks</description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Thread Safety:</b> Uses ConcurrentDictionary for safe concurrent access from
    ///         Unity main thread and EventHub background threads.
    ///     </para>
    /// </remarks>
    public sealed class ChatPresentationStrategy : ITranscriptPresentationStrategy
    {
        private readonly ConcurrentDictionary<string, AggregatedMessage> _activeMessages = new();
        private readonly ILogger _logger;
        private bool _disposed;
        private string _lastSpeakerId;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ChatPresentationStrategy" /> class.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostics.</param>
        public ChatPresentationStrategy(ILogger logger = null)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public event Action<TranscriptViewModel> OnMessageUpdated = delegate { };

        /// <inheritdoc />
        public event Action<string> OnMessageCompleted = delegate { };

        /// <inheritdoc />
        public void HandleMessage(TranscriptViewModel viewModel)
        {
            if (_disposed) return;

            if (viewModel.Speaker == TranscriptSpeaker.Player)
            {
                ConvaiLogger.Debug(
                    $"[ChatPresentationStrategy] Player message received: isFinal={viewModel.IsFinal}, text=\"{viewModel.Text}\"",
                    LogCategory.Player);
            }

            _logger?.Debug(
                $"[ChatPresentationStrategy] HandleMessage: Speaker={viewModel.Speaker}, SpeakerId={viewModel.SpeakerId}, IsFinal={viewModel.IsFinal}, Text=\"{viewModel.Text}\"");

            if (viewModel.Speaker == TranscriptSpeaker.Character &&
                string.IsNullOrEmpty(viewModel.Text) &&
                !_activeMessages.ContainsKey(viewModel.SpeakerId))
            {
                _logger?.Debug("[ChatPresentationStrategy] Skipping empty character message with no active entry");
                return;
            }

            string speakerId = viewModel.SpeakerId;

            if (!string.IsNullOrEmpty(_lastSpeakerId) &&
                _lastSpeakerId != speakerId &&
                _activeMessages.TryGetValue(_lastSpeakerId, out AggregatedMessage previousMessage) &&
                previousMessage.Speaker == TranscriptSpeaker.Character)
                CompletePreviousMessage(previousMessage);

            _lastSpeakerId = speakerId;

            bool isNewMessage = false;
            AggregatedMessage aggregated = _activeMessages.GetOrAdd(speakerId, _ =>
            {
                isNewMessage = true;
                return new AggregatedMessage(viewModel);
            });

            if (isNewMessage)
                _logger?.Debug($"[ChatPresentationStrategy] Created NEW message entry for SpeakerId={speakerId}");

            if (!isNewMessage)
            {
                if (!string.IsNullOrEmpty(viewModel.Text))
                {
                    if (viewModel.Speaker == TranscriptSpeaker.Player)
                    {
                        if (viewModel.IsFinal)
                        {
                            _logger?.Debug($"[ChatPresentationStrategy] Player FINAL received: \"{viewModel.Text}\"");
                            aggregated.ReplaceFinalText(viewModel.Text);
                        }
                        else
                        {
                            _logger?.Debug($"[ChatPresentationStrategy] Player interim received: \"{viewModel.Text}\"");
                            aggregated.ReplaceInterimText(viewModel.Text);
                        }
                    }
                    else if (viewModel.IsFinal)
                    {
                        if (!aggregated.EndsWithText(viewModel.Text)) aggregated.AppendText(viewModel.Text);
                    }
                    else
                        aggregated.AppendText(viewModel.Text);
                }

                if (viewModel.IsFinal) aggregated.UpdateFinalState(true);
            }

            TranscriptViewModel updatedViewModel = aggregated.ToViewModel();
            OnMessageUpdated.Invoke(updatedViewModel);
        }

        /// <inheritdoc />
        public void CompletePlayerTurn()
        {
            if (_disposed) return;

            _logger?.Debug("[ChatPresentationStrategy] CompletePlayerTurn called");

            List<string> playerMessageKeys = new();
            foreach (KeyValuePair<string, AggregatedMessage> kvp in _activeMessages)
            {
                if (kvp.Value.Speaker == TranscriptSpeaker.Player)
                    playerMessageKeys.Add(kvp.Key);
            }

            foreach (string key in playerMessageKeys)
            {
                if (_activeMessages.TryGetValue(key, out AggregatedMessage message))
                {
                    _logger?.Debug(
                        $"[ChatPresentationStrategy] Completing player message: \"{message.GetFullText()}\"");
                    CompletePreviousMessage(message);
                }
            }
        }

        /// <inheritdoc />
        public void CompleteCharacterTurn(string characterId)
        {
            if (_disposed) return;

            _logger?.Debug(
                $"[ChatPresentationStrategy] CompleteCharacterTurn called for characterId: \"{characterId ?? "(all)"}\"");

            if (string.IsNullOrEmpty(characterId))
            {
                List<string> characterMessageKeys = new();
                foreach (KeyValuePair<string, AggregatedMessage> kvp in _activeMessages)
                {
                    if (kvp.Value.Speaker == TranscriptSpeaker.Character)
                        characterMessageKeys.Add(kvp.Key);
                }

                foreach (string key in characterMessageKeys)
                {
                    if (_activeMessages.TryGetValue(key, out AggregatedMessage message))
                    {
                        _logger?.Debug($"[ChatPresentationStrategy] Completing character message for: {key}");
                        CompletePreviousMessage(message);
                    }
                }
            }
            else if (_activeMessages.TryGetValue(characterId, out AggregatedMessage message) &&
                     message.Speaker == TranscriptSpeaker.Character)
            {
                _logger?.Debug($"[ChatPresentationStrategy] Completing character message for: {characterId}");
                CompletePreviousMessage(message);
            }
        }

        /// <inheritdoc />
        public bool HasActivePlayerMessage()
        {
            foreach (KeyValuePair<string, AggregatedMessage> kvp in _activeMessages)
            {
                if (kvp.Value.Speaker == TranscriptSpeaker.Player)
                    return true;
            }

            return false;
        }

        /// <inheritdoc />
        public void ClearAll()
        {
            foreach (KeyValuePair<string, AggregatedMessage> kvp in _activeMessages) OnMessageCompleted.Invoke(kvp.Key);
            _activeMessages.Clear();
            _lastSpeakerId = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activeMessages.Clear();
            _lastSpeakerId = null;
        }

        private void CompletePreviousMessage(AggregatedMessage message)
        {
            TranscriptViewModel viewModel = message.ToViewModel();
            OnMessageUpdated.Invoke(viewModel);

            OnMessageCompleted.Invoke(message.SpeakerId);
            _activeMessages.TryRemove(message.SpeakerId, out _);
        }

        /// <summary>
        ///     Internal class to track aggregated message state.
        /// </summary>
        private sealed class AggregatedMessage
        {
            private readonly StringBuilder _finalizedTextBuilder = new();
            private readonly StringBuilder _interimTextBuilder = new();
            private readonly TranscriptViewModel _originalViewModel;
            private bool _isFinal;

            public AggregatedMessage(TranscriptViewModel viewModel)
            {
                _originalViewModel = viewModel;

                if (!string.IsNullOrEmpty(viewModel.Text))
                {
                    if (viewModel.Speaker == TranscriptSpeaker.Character || viewModel.IsFinal)
                        _finalizedTextBuilder.Append(viewModel.Text);
                    else
                        _interimTextBuilder.Append(viewModel.Text);
                }

                _isFinal = viewModel.IsFinal;
            }

            public string SpeakerId => _originalViewModel.SpeakerId;
            public TranscriptSpeaker Speaker => _originalViewModel.Speaker;
            public string DisplayName => _originalViewModel.DisplayName;

            public void AppendText(string text)
            {
                if (!string.IsNullOrEmpty(text)) _finalizedTextBuilder.Append(text);
            }

            public void ReplaceInterimText(string text)
            {
                _finalizedTextBuilder.Clear();
                _interimTextBuilder.Clear();
                if (!string.IsNullOrEmpty(text)) _interimTextBuilder.Append(text);
            }

            public void ReplaceFinalText(string text)
            {
                if (string.IsNullOrEmpty(text)) return;

                _finalizedTextBuilder.Clear();
                _interimTextBuilder.Clear();
                _finalizedTextBuilder.Append(text);
            }

            public void SetFinalText(string text)
            {
                _interimTextBuilder.Clear();
                _finalizedTextBuilder.Clear();
                if (!string.IsNullOrEmpty(text)) _finalizedTextBuilder.Append(text);
                _isFinal = true;
            }

            public bool EndsWithText(string text)
            {
                if (string.IsNullOrEmpty(text)) return true;
                string currentText = GetFullText();
                return currentText.EndsWith(text, StringComparison.Ordinal);
            }

            public void UpdateFinalState(bool isFinal) => _isFinal = _isFinal || isFinal;

            public string GetFullText()
            {
                if (_interimTextBuilder.Length == 0) return _finalizedTextBuilder.ToString();

                if (_finalizedTextBuilder.Length == 0) return _interimTextBuilder.ToString();

                string finalized = _finalizedTextBuilder.ToString();
                string interim = _interimTextBuilder.ToString();

                char lastChar = finalized[finalized.Length - 1];
                if (char.IsWhiteSpace(lastChar)) return finalized + interim;
                return finalized + " " + interim;
            }

            public TranscriptViewModel ToViewModel()
            {
                string aggregatedText = GetFullText();

                var message = TranscriptMessage.Create(
                    SpeakerId,
                    DisplayName,
                    aggregatedText,
                    _isFinal
                );

                return new TranscriptViewModel(Speaker, message, aggregatedText);
            }
        }
    }
}
