namespace Convai.Shared.Interfaces
{
    /// <summary>
    ///     Cross-assembly contract for video publisher components.
    /// </summary>
    /// <remarks>
    ///     Implemented by MonoBehaviour components that publish video to the room.
    ///     This interface enables compile-time safe type detection across assembly
    ///     boundaries (e.g., ConvaiRoomManager detecting ConvaiVideoPublisher
    ///     without referencing the Vision module assembly).
    ///     Implementing classes: ConvaiVideoPublisher in Convai.Modules.Vision.
    /// </remarks>
    public interface IVideoPublisher
    {
        /// <summary>
        ///     Gets the configured video track name used for publishing.
        /// </summary>
        public string VideoTrackName { get; }
    }
}
