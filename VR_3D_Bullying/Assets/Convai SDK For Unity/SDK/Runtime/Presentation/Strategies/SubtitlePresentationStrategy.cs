using System;
using Convai.Domain.Logging;
using Convai.Runtime.Presentation.Presenters;

namespace Convai.Runtime.Presentation.Strategies
{
    /// <summary>
    ///     Subtitle-style presentation strategy that shows streaming text with auto-clear.
    ///     Each message replaces the previous - no aggregation or history.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Behavior:</b>
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>Shows the latest transcript text as a subtitle overlay</description>
    ///             </item>
    ///             <item>
    ///                 <description>Interim and final messages both update the display</description>
    ///             </item>
    ///             <item>
    ///                 <description>Emits completion signal on final character messages for auto-hide</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public sealed class SubtitlePresentationStrategy : ITranscriptPresentationStrategy
    {
        private readonly ILogger _logger;
        private string _currentMessageId;
        private bool _disposed;
        private bool _hasActivePlayerMessage;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SubtitlePresentationStrategy" /> class.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostics.</param>
        public SubtitlePresentationStrategy(ILogger logger = null)
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
                $"[SubtitlePresentationStrategy] HandleMessage: Speaker={viewModel.Speaker}, IsFinal={viewModel.IsFinal}, Text=\"{viewModel.Text}\"");

            if (viewModel.Speaker == TranscriptSpeaker.Player) _hasActivePlayerMessage = !viewModel.IsFinal;

            _currentMessageId = viewModel.SpeakerId;

            OnMessageUpdated.Invoke(viewModel);

            if (viewModel.Speaker == TranscriptSpeaker.Character && viewModel.IsFinal)
                OnMessageCompleted.Invoke(viewModel.SpeakerId);
        }

        /// <inheritdoc />
        public void CompletePlayerTurn()
        {
            if (_disposed) return;

            _logger?.Debug("[SubtitlePresentationStrategy] CompletePlayerTurn - subtitles auto-hide, no action needed");

            _hasActivePlayerMessage = false;
        }

        /// <inheritdoc />
        public void CompleteCharacterTurn(string characterId)
        {
            if (_disposed) return;

            _logger?.Debug(
                "[SubtitlePresentationStrategy] CompleteCharacterTurn - subtitles auto-hide, no action needed");
        }

        /// <inheritdoc />
        public bool HasActivePlayerMessage() => _hasActivePlayerMessage;

        /// <inheritdoc />
        public void ClearAll()
        {
            if (!string.IsNullOrEmpty(_currentMessageId)) OnMessageCompleted.Invoke(_currentMessageId);
            _currentMessageId = null;
            _hasActivePlayerMessage = false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _currentMessageId = null;
            _hasActivePlayerMessage = false;
        }
    }
}
