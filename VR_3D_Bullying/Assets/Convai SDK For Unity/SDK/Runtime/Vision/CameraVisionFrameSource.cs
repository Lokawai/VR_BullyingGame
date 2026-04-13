using System.Threading;
using Convai.Domain.DomainEvents.Vision;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Shared;
using Convai.Shared.DependencyInjection;
using UnityEngine;

namespace Convai.Runtime.Vision
{
    /// <summary>
    ///     Vision frame source that captures from a Unity Camera component.
    ///     Implements <see cref="IVisionFrameSource" /> for video streaming and debug preview.
    ///     Publishes domain events via EventHub when capture state changes.
    /// </summary>
    /// <remarks>
    ///     This is the recommended component for capturing in-game visuals (what the player sees).
    ///     It uses ping-pong render targets for efficient GPU capture and
    ///     publishes events via EventHub for loose coupling.
    ///     Settings Priority:
    ///     1. Inspector overrides (if > 0)
    ///     2. ConvaiSettings values
    /// </remarks>
    [AddComponentMenu("Convai/Vision/Camera Vision Frame Source")]
    public class CameraVisionFrameSource : MonoBehaviour, IVisionFrameSource, IInjectable
    {
        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            container.TryGet(out IEventHub eventHub);
            _eventHub = eventHub;
        }

        #region Inspector Fields

#pragma warning disable CS0649
        [Header("Capture Settings (0 = use ConvaiSettings)")]
        [Tooltip("Capture width in pixels. 0 uses ConvaiSettings.VisionCaptureWidth.")]
        [SerializeField]
        private int _captureWidth;

        [Tooltip("Capture height in pixels. 0 uses ConvaiSettings.VisionCaptureHeight.")] [SerializeField]
        private int _captureHeight;

        [Tooltip("Target frames per second. 0 uses ConvaiSettings.VisionFrameRate.")] [SerializeField]
        private int _targetFps;
#pragma warning restore CS0649

        #endregion

        #region Private Fields

        private RenderTexture _rtA, _rtB;
        private RenderTexture _rtFlipped;
        private bool _useA;
        private float _nextCaptureTime;

        private int _effectiveWidth;
        private int _effectiveHeight;
        private int _effectiveFps;
        private bool _isInitialized;

        private IEventHub _eventHub;
        private CancellationTokenSource _captureCts;

        #endregion

        #region Public Properties

        /// <summary>Gets the target camera for capture.</summary>
        public Camera TargetCamera
        {
            get
            {
                if (_targetCamera == null) ResolveCamera();
                return _targetCamera;
            }
            private set => _targetCamera = value;
        }

        [Header("Camera")] [Tooltip("Camera to capture from. Uses Camera.main if not set.")] [SerializeField]
        private Camera _targetCamera;

        /// <summary>Gets the effective capture width.</summary>
        public int EffectiveWidth
        {
            get
            {
                EnsureInitialized();
                return _effectiveWidth;
            }
        }

        /// <summary>Gets the effective capture height.</summary>
        public int EffectiveHeight
        {
            get
            {
                EnsureInitialized();
                return _effectiveHeight;
            }
        }

        /// <summary>Gets the effective frame rate.</summary>
        public int EffectiveFps
        {
            get
            {
                EnsureInitialized();
                return _effectiveFps;
            }
        }

        /// <summary>Gets the current frame count since capture started.</summary>
        public long FrameCount { get; private set; }

        /// <summary>Gets the source identifier.</summary>
        [field: Header("Debug")]
        [field: Tooltip("Optional identifier for multi-camera scenarios.")]
        public string SourceId { get; }

        /// <summary>Gets the current render texture (Y-flipped for correct orientation).</summary>
        public RenderTexture CurrentRenderTexture
        {
            get
            {
                EnsureInitialized();
                return _rtFlipped;
            }
        }

        #endregion

        #region IVisionFrameSource Implementation

        /// <inheritdoc />
        public bool IsCapturing { get; private set; }

        /// <inheritdoc />
        (int Width, int Height) IVisionFrameSource.FrameDimensions
        {
            get
            {
                EnsureInitialized();
                return (_effectiveWidth, _effectiveHeight);
            }
        }

        /// <inheritdoc />
        float IVisionFrameSource.TargetFrameRate
        {
            get
            {
                EnsureInitialized();
                return _effectiveFps;
            }
        }

        /// <inheritdoc />
        public void StartCapture()
        {
            EnsureInitialized();
            if (IsCapturing) return;

            IsCapturing = true;
            FrameCount = 0;
            _captureCts = new CancellationTokenSource();

            _eventHub?.Publish(VisionCaptureStarted.Create(
                _effectiveWidth,
                _effectiveHeight,
                _effectiveFps,
                SourceId));

            ConvaiLogger.Info(
                $"[CameraVisionFrameSource] Capture started: {_effectiveWidth}x{_effectiveHeight} @ {_effectiveFps}fps",
                LogCategory.Vision);
        }

        /// <inheritdoc />
        public void StopCapture() => StopCapture(VisionCaptureStopReason.UserRequested);

        #endregion

        #region Unity Lifecycle

        private void Awake() => EnsureInitialized();

        private void EnsureInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            ResolveCamera();
            ResolveSettings();
            InitializeRenderTargets();
            ResolveEventHub();
        }

        private void ResolveCamera()
        {
            if (_targetCamera != null) return;
            _targetCamera = GetComponent<Camera>();
            if (_targetCamera == null) _targetCamera = Camera.main;
        }

        private void OnEnable() => EnsureInitialized();

        private void LateUpdate()
        {
            if (!IsCapturing) return;

            if (Time.time < _nextCaptureTime) return;
            _nextCaptureTime = Time.time + (1f / _effectiveFps);

            CaptureFrame();
        }

        private void OnDisable()
        {
            if (IsCapturing) StopCapture(VisionCaptureStopReason.ComponentDisabled);
        }

        private void OnDestroy() => CleanupRenderTargets();

        #endregion

        #region Private Methods

        private void ResolveSettings()
        {
            const int DefaultWidth = 1280;
            const int DefaultHeight = 720;
            const int DefaultFps = 15;

            var settings = ConvaiSettings.Instance;
            _effectiveWidth = _captureWidth > 0 ? _captureWidth :
                settings != null ? settings.VisionCaptureWidth : DefaultWidth;
            _effectiveHeight = _captureHeight > 0 ? _captureHeight :
                settings != null ? settings.VisionCaptureHeight : DefaultHeight;
            _effectiveFps = _targetFps > 0 ? _targetFps : settings != null ? settings.VisionFrameRate : DefaultFps;

            ConvaiLogger.Info(
                $"[CameraVisionFrameSource] Resolved settings: {_effectiveWidth}x{_effectiveHeight} @ {_effectiveFps}fps",
                LogCategory.Vision);
        }

        private void InitializeRenderTargets()
        {
            _rtA = CreateRenderTexture();
            _rtB = CreateRenderTexture();
            _rtFlipped = new RenderTexture(_effectiveWidth, _effectiveHeight, 24, RenderTextureFormat.ARGB32);
            _rtFlipped.Create();
        }

        private RenderTexture CreateRenderTexture()
        {
            var rt = new RenderTexture(_effectiveWidth, _effectiveHeight, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            return rt;
        }

        private void ResolveEventHub()
        {
            // EventHub is resolved via InjectServices(). This method is kept
            // as a no-op so EnsureInitialized() call order is unchanged.
        }

        private void CaptureFrame()
        {
            if (TargetCamera == null)
            {
                ConvaiLogger.Error("[CameraVisionFrameSource] Camera lost during capture", LogCategory.Vision);
                StopCapture(
                    VisionCaptureStopReason.CameraLost,
                    SessionErrorCodes.GetDescription(SessionErrorCodes.VisionCameraLost),
                    SessionErrorCodes.VisionCameraLost);
                return;
            }

            if (_rtFlipped == null)
            {
                _rtFlipped = new RenderTexture(_effectiveWidth, _effectiveHeight, 0, RenderTextureFormat.ARGB32);
                if (!_rtFlipped.Create())
                {
                    ConvaiLogger.Error("[CameraVisionFrameSource] Failed to create RenderTexture", LogCategory.Vision);
                    StopCapture(
                        VisionCaptureStopReason.Error,
                        SessionErrorCodes.GetDescription(SessionErrorCodes.VisionRenderTextureFailed),
                        SessionErrorCodes.VisionRenderTextureFailed);
                    return;
                }
            }

            RenderTexture rt = _useA ? _rtA : _rtB;
            _useA = !_useA;

            RenderTexture prev = TargetCamera.targetTexture;
            TargetCamera.targetTexture = rt;
            TargetCamera.Render();
            TargetCamera.targetTexture = prev;

            Graphics.Blit(rt, _rtFlipped, new Vector2(1, -1), new Vector2(0, 1));

            FrameCount++;

            _eventHub?.Publish(VisionFrameCaptured.Create(
                _effectiveWidth,
                _effectiveHeight,
                FrameCount,
                0,
                SourceId));
        }

        private void StopCapture(VisionCaptureStopReason reason, string errorMessage = null, string errorCode = null)
        {
            if (!IsCapturing) return;

            IsCapturing = false;
            _captureCts?.Cancel();
            _captureCts?.Dispose();
            _captureCts = null;

            _eventHub?.Publish(VisionCaptureStopped.Create(
                FrameCount,
                reason,
                SourceId,
                errorMessage,
                errorCode));

            if (errorCode != null)
            {
                ConvaiLogger.Error(
                    $"[CameraVisionFrameSource] Capture stopped with error: {FrameCount} frames, reason: {reason}, errorCode: {errorCode}",
                    LogCategory.Vision);
            }
            else
            {
                ConvaiLogger.Info($"[CameraVisionFrameSource] Capture stopped: {FrameCount} frames, reason: {reason}",
                    LogCategory.Vision);
            }
        }

        private void CleanupRenderTargets()
        {
            if (_rtA != null)
            {
                Destroy(_rtA);
                _rtA = null;
            }

            if (_rtB != null)
            {
                Destroy(_rtB);
                _rtB = null;
            }

            if (_rtFlipped != null)
            {
                Destroy(_rtFlipped);
                _rtFlipped = null;
            }
        }

        #endregion
    }
}
