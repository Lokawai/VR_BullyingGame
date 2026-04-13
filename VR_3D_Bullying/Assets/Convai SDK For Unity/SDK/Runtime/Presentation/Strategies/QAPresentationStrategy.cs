using System;
using Convai.Domain.Logging;
using Convai.Runtime.Presentation.Presenters;

namespace Convai.Runtime.Presentation.Strategies
{
    /// <summary>
    ///     Question-Answer presentation strategy for displaying Q&amp;A style transcripts.
    ///     Shows player question and character answer as a pair.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Behavior:</b>
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Forwards all messages for UI display</description>
    ///             </item>
    ///             <item>
    ///                 <description>Emits completion when character sends a final response</description>
    ///             </item>
    ///             <item>
    ///                 <description>Designed for simple Q&amp;A interfaces (question above, answer below)</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public sealed class QAPresentationStrategy : ITranscriptPresentationStrategy
    {
        private readonly ILogger _logger;
        private string _currentAnswerId;
        private string _currentQuestionId;
        private bool _disposed;
        private bool _hasActivePlayerMessage;

        /// <summary>
        ///     Initializes a new instance of the <see cref="QAPresentationStrategy" /> class.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostics.</param>
        public QAPresentationStrategy(ILogger logger = null)
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

            _logger?.Debug(
                $"[QAPresentationStrategy] HandleMessage: Speaker={viewModel.Speaker}, IsFinal={viewModel.IsFinal}, Text=\"{viewModel.Text}\"");

            if (viewModel.Speaker == TranscriptSpeaker.Player)
            {
                _currentQuestionId = viewModel.SpeakerId;
                _hasActivePlayerMessage = !viewModel.IsFinal;
            }
            else
                _currentAnswerId = viewModel.SpeakerId;

            OnMessageUpdated.Invoke(viewModel);

            if (viewModel.Speaker == TranscriptSpeaker.Character && viewModel.IsFinal)
                OnMessageCompleted.Invoke(viewModel.SpeakerId);
        }

        /// <inheritdoc />
        public void CompletePlayerTurn()
        {
            if (_disposed) return;

            _logger?.Debug("[QAPresentationStrategy] CompletePlayerTurn");

            _hasActivePlayerMessage = false;
        }

        /// <inheritdoc />
        public void CompleteCharacterTurn(string characterId)
        {
            if (_disposed) return;

            _logger?.Debug($"[QAPresentationStrategy] CompleteCharacterTurn for: \"{characterId ?? "(all)"}\"");

            if (!string.IsNullOrEmpty(_currentAnswerId))
            {
                OnMessageCompleted.Invoke(_currentAnswerId);
                _currentAnswerId = null;
            }
        }

        /// <inheritdoc />
        public bool HasActivePlayerMessage() => _hasActivePlayerMessage;

        /// <inheritdoc />
        public void ClearAll()
        {
            if (!string.IsNullOrEmpty(_currentQuestionId)) OnMessageCompleted.Invoke(_currentQuestionId);
            if (!string.IsNullOrEmpty(_currentAnswerId)) OnMessageCompleted.Invoke(_currentAnswerId);
            _currentQuestionId = null;
            _currentAnswerId = null;
            _hasActivePlayerMessage = false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _currentQuestionId = null;
            _currentAnswerId = null;
            _hasActivePlayerMessage = false;
        }
    }
}
