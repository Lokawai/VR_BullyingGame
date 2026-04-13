namespace Convai.Domain.Models
{
    /// <summary>
    ///     Configuration settings for vision capture.
    /// </summary>
    /// <remarks>
    ///     This struct provides a consistent set of configuration values for vision capture.
    ///     It's used by both the Unity layer (CameraVisionFrameSource) and Application layer (VisionService)
    ///     to ensure consistent settings across the vision pipeline.
    ///     Default values are optimized for:
    ///     - Good visual quality (1280x720)
    ///     - Low bandwidth (15 FPS, 75% JPEG quality)
    ///     - Minimal latency
    ///     Usage:
    ///     <code>
    /// 
    /// VisionCaptureSettings settings = VisionCaptureSettings.Default;
    /// 
    /// 
    /// VisionCaptureSettings settings = VisionCaptureSettings.Default
    ///     .WithResolution(1920, 1080)
    ///     .WithFrameRate(30)
    ///     .WithJpegQuality(85);
    /// </code>
    /// </remarks>
    public readonly struct VisionCaptureSettings
    {
        /// <summary>
        ///     Default capture settings (1280x720, 15 FPS, 75% quality).
        /// </summary>
        public static readonly VisionCaptureSettings Default = new(
            1280,
            720,
            15,
            75,
            null
        );

        /// <summary>
        ///     Low quality preset (640x480, 10 FPS, 60% quality).
        ///     Suitable for bandwidth-constrained scenarios.
        /// </summary>
        public static readonly VisionCaptureSettings LowQuality = new(
            640,
            480,
            10,
            60,
            null
        );

        /// <summary>
        ///     High quality preset (1920x1080, 30 FPS, 90% quality).
        ///     Suitable for high-bandwidth, high-detail scenarios.
        /// </summary>
        public static readonly VisionCaptureSettings HighQuality = new(
            1920,
            1080,
            30,
            90,
            null
        );

        /// <summary>
        ///     Capture width in pixels. Must be > 0.
        /// </summary>
        public int Width { get; }

        /// <summary>
        ///     Capture height in pixels. Must be > 0.
        /// </summary>
        public int Height { get; }

        /// <summary>
        ///     Target capture frame rate in frames per second. Must be > 0.
        /// </summary>
        public int FrameRate { get; }

        /// <summary>
        ///     JPEG compression quality (1-100). Higher = better quality, larger size.
        /// </summary>
        public int JpegQuality { get; }

        /// <summary>
        ///     Name of the camera to use. Null means use the main camera.
        /// </summary>
        public string CameraName { get; }

        /// <summary>
        ///     Creates a new VisionCaptureSettings instance.
        /// </summary>
        public VisionCaptureSettings(
            int width,
            int height,
            int frameRate,
            int jpegQuality,
            string cameraName)
        {
            Width = width > 0 ? width : 1280;
            Height = height > 0 ? height : 720;
            FrameRate = frameRate > 0 ? frameRate : 15;
            JpegQuality = jpegQuality > 0 && jpegQuality <= 100 ? jpegQuality : 75;
            CameraName = cameraName;
        }

        /// <summary>
        ///     Creates a new settings instance with different resolution.
        /// </summary>
        public VisionCaptureSettings WithResolution(int width, int height) =>
            new(width, height, FrameRate, JpegQuality, CameraName);

        /// <summary>
        ///     Creates a new settings instance with different frame rate.
        /// </summary>
        public VisionCaptureSettings WithFrameRate(int frameRate) =>
            new(Width, Height, frameRate, JpegQuality, CameraName);

        /// <summary>
        ///     Creates a new settings instance with different JPEG quality.
        /// </summary>
        public VisionCaptureSettings WithJpegQuality(int quality) => new(Width, Height, FrameRate, quality, CameraName);

        /// <summary>
        ///     Creates a new settings instance with a specific camera name.
        /// </summary>
        public VisionCaptureSettings WithCamera(string cameraName) =>
            new(Width, Height, FrameRate, JpegQuality, cameraName);

        /// <summary>
        ///     Gets the aspect ratio of the capture resolution.
        /// </summary>
        public float AspectRatio => Height > 0 ? (float)Width / Height : 0f;

        /// <summary>
        ///     Gets the total pixels per frame.
        /// </summary>
        public int TotalPixels => Width * Height;

        /// <summary>
        ///     Estimates the raw uncompressed frame size in bytes (RGBA32).
        /// </summary>
        public int EstimatedRawSizeBytes => TotalPixels * 4;

        /// <summary>
        ///     Returns a string representation for debugging.
        /// </summary>
        public override string ToString() =>
            $"VisionCaptureSettings({Width}x{Height} @ {FrameRate}fps, JPEG {JpegQuality}%)";
    }
}
