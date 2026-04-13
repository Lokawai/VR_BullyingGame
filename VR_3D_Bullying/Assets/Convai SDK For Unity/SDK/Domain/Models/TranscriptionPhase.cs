namespace Convai.Domain.Models
{
    /// <summary>
    ///     Represents the phase of a player transcription session.
    ///     Used by the Application layer to make decisions about filtering, aggregation, and display.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Phase Flow:</b>
    ///         <code>
    /// Idle → Listening → Interim* → AsrFinal → ProcessedFinal? → Completed → Idle
    /// </code>
    ///     </para>
    ///     <para>
    ///         <b>Application Layer Responsibilities:</b>
    ///         <list type="bullet">
    ///             <item>
    ///                 <description><c>Idle</c>, <c>Listening</c>: Suppress (no display value)</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>Interim</c>: Display as live transcription (IsFinal=false)</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>AsrFinal</c>: Buffer for potential use in final text</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>ProcessedFinal</c>: Buffer for potential use in final text (preferred over AsrFinal)</description>
    ///             </item>
    ///             <item>
    ///                 <description><c>Completed</c>: Emit final transcript using best available text</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public enum TranscriptionPhase
    {
        /// <summary>
        ///     No transcription is active.
        /// </summary>
        Idle,

        /// <summary>
        ///     The system detected speech onset and is preparing to stream text.
        /// </summary>
        Listening,

        /// <summary>
        ///     The player transcript is streaming; the text is not yet final.
        /// </summary>
        Interim,

        /// <summary>
        ///     Automatic speech recognition produced a final hypothesis (subject to further processing).
        ///     This is the raw ASR output before any post-processing.
        /// </summary>
        AsrFinal,

        /// <summary>
        ///     The transcript has been post-processed/cleaned by the server.
        ///     Preferred over AsrFinal when both are available.
        /// </summary>
        ProcessedFinal,

        /// <summary>
        ///     The transcription session ended (either naturally or due to cancellation).
        ///     The Application layer should use ProcessedFinal if available, otherwise AsrFinal.
        /// </summary>
        Completed
    }
}
