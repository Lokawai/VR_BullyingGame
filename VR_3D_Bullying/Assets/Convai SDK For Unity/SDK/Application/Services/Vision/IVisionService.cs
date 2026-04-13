using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Models;

namespace Convai.Application.Services.Vision
{
    /// <summary>
    ///     Application-layer service interface for Vision operations.
    ///     Manages vision state transitions and publishes domain events.
    /// </summary>
    /// <remarks>
    ///     This service provides a high-level API for:
    ///     - Enabling/disabling vision capture
    ///     - Querying vision state
    ///     Video publishing is handled by Unity layer components (ConvaiVideoPublisher)
    ///     that consume RenderTexture from IVisionFrameSource implementations.
    ///     Usage:
    ///     <code>
    /// 
    /// await visionService.EnableAsync();
    /// 
    /// 
    /// VisionCaptureSettings settings = new VisionCaptureSettings(1280, 720, 30, 85);
    /// await visionService.EnableAsync(settings);
    /// 
    /// 
    /// await visionService.DisableAsync();
    /// </code>
    /// </remarks>
    public interface IVisionService
    {
        /// <summary>
        ///     Gets the current vision state.
        /// </summary>
        public VisionState State { get; }

        /// <summary>
        ///     Gets a value indicating whether vision is enabled (capturing and/or publishing).
        /// </summary>
        public bool IsEnabled { get; }

        /// <summary>
        ///     Gets the current capture settings, or null if vision is disabled.
        /// </summary>
        public VisionCaptureSettings? CurrentSettings { get; }

        /// <summary>
        ///     Enables vision capture and video publishing using settings from ConvaiSettings.
        /// </summary>
        /// <param name="ct">Cancellation token to cancel the operation.</param>
        /// <returns>True if vision was enabled successfully; false otherwise.</returns>
        public Task<bool> EnableAsync(CancellationToken ct = default);

        /// <summary>
        ///     Enables vision capture and video publishing with custom settings.
        /// </summary>
        /// <param name="settings">Custom capture settings.</param>
        /// <param name="ct">Cancellation token to cancel the operation.</param>
        /// <returns>True if vision was enabled successfully; false otherwise.</returns>
        public Task<bool> EnableAsync(VisionCaptureSettings settings, CancellationToken ct = default);

        /// <summary>
        ///     Disables vision capture and unpublishes the video track.
        /// </summary>
        /// <param name="ct">Cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when vision is disabled.</returns>
        public Task DisableAsync(CancellationToken ct = default);
    }

    /// <summary>
    ///     Represents the current state of the vision system.
    /// </summary>
    public enum VisionState
    {
        /// <summary>Vision is disabled and not capturing.</summary>
        Disabled = 0,

        /// <summary>Vision is initializing capture resources.</summary>
        Initializing = 1,

        /// <summary>Vision is actively capturing frames.</summary>
        Capturing = 2,

        /// <summary>Vision is capturing and publishing to the room.</summary>
        Publishing = 3,

        /// <summary>Vision encountered an error.</summary>
        Error = 4
    }
}
