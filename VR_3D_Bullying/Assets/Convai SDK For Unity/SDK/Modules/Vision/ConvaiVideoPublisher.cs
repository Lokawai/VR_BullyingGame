using System;
using System.Threading.Tasks;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Infrastructure.Networking;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using Convai.Runtime.Vision;
using Convai.Shared;
using Convai.Shared.DependencyInjection;
using Convai.Shared.Interfaces;
using UnityEngine;

namespace Convai.Modules.Vision
{
    /// <summary>
    ///     Publishes visual context to the LiveKit room.
    ///     Uses <see cref="IVideoTrackManager" /> for track lifecycle management.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On native platforms, this component works with any vision source implementing <see cref="IVisionFrameSource" />
    ///         :
    ///         <list type="bullet">
    ///             <item><see cref="CameraVisionFrameSource" /> - Unity Camera capture (default)</item>
    ///             <item><see cref="WebcamVisionFrameSource" /> - Physical webcam capture</item>
    ///             <item>Custom implementations - Video files, screen capture, AR passthrough, etc.</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         <b>Setup Options:</b>
    ///         <list type="number">
    ///             <item>Assign a frame source in the Inspector (recommended for native platforms)</item>
    ///             <item>Add CameraVisionFrameSource to the same GameObject (auto-detected on native platforms)</item>
    ///             <item>Let the component find any IVisionFrameSource in the scene (native platforms)</item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         On WebGL, the publisher ignores <see cref="IVisionFrameSource" /> components and captures the
    ///         visible Unity canvas in the browser instead.
    ///     </para>
    ///     <para>
    ///         Requires dependency injection via the ConvaiManager pipeline.
    ///         The video track is automatically published when the room connects
    ///         and unpublished when the component is destroyed.
    ///     </para>
    /// </remarks>
    public class ConvaiVideoPublisher : MonoBehaviour, IVideoPublisher, IInjectable
    {
        [Header("Frame Source")]
        [Tooltip(
            "Vision frame source to publish on native platforms. WebGL publishes the visible Unity canvas instead of a Unity RenderTexture source.")]
        [SerializeField]
        private MonoBehaviour _frameSourceComponent;

        [Header("Video Settings")] [Tooltip("Name of the video track as it appears in the LiveKit room")]
        public string videoTrackName = "unity-scene";

        [Tooltip("Maximum frame rate for video encoding")] [Range(1, 30)]
        public int frameRate = 15;

        [Tooltip("Maximum bitrate in bits per second")]
        public int maxBitrate = 1_000_000;

        private bool _connectionCallbacksRegistered;
        private IEventHub _eventHub;

        private bool _isInjected;
        private IConvaiRoomConnectionService _roomConnectionService;
        private IVideoSourceFactory _videoSourceFactory;
        private IVideoTrackManager _videoTrackManager;

        /// <summary>
        ///     Gets a value indicating whether a video track is currently being published.
        /// </summary>
        public bool IsPublishing => _videoTrackManager?.IsPublishing ?? false;

        /// <summary>
        ///     Gets the current frame source being used.
        /// </summary>
        public IVisionFrameSource FrameSource { get; private set; }

        private void Start()
        {
            if (UsesWebGLCanvasPublishPath())
            {
                if (_frameSourceComponent != null)
                {
                    ConvaiLogger.Info(
                        "[ConvaiVideoPublisher] WebGL publish path ignores assigned IVisionFrameSource and uses the visible Unity canvas instead.",
                        LogCategory.Vision);
                }
            }
            else
            {
                ResolveFrameSource();

                if (FrameSource == null)
                {
                    ConvaiLogger.Error(
                        "[ConvaiVideoPublisher] No IVisionFrameSource found. Add CameraVisionFrameSource or assign a frame source.",
                        LogCategory.Vision);
                    enabled = false;
                    return;
                }
            }

            if (!_isInjected || _roomConnectionService == null)
            {
                ConvaiLogger.Error("[ConvaiVideoPublisher] Dependencies not injected. Add ConvaiManager to scene.",
                    LogCategory.Vision);
                enabled = false;
                return;
            }

            RegisterConnectionCallbacks();

            if (_roomConnectionService.IsConnected) OnRoomConnected();
        }

        private void OnDestroy()
        {
            UnregisterConnectionCallbacks();

            if (FrameSource != null && FrameSource.IsCapturing)
            {
                FrameSource.StopCapture();
                ConvaiLogger.Info("[ConvaiVideoPublisher] Vision capture stopped on destroy", LogCategory.Vision);
            }

            if (_videoTrackManager == null) return;

            IVideoTrackManager manager = _videoTrackManager;
            _videoTrackManager = null;
            _ = UnpublishAndDisposeAsync(manager);
        }

        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            _eventHub = container.Get<IEventHub>();
            _videoSourceFactory = container.Get<IVideoSourceFactory>();
            Inject(container.Get<IConvaiRoomConnectionService>());
        }

        /// <summary>
        ///     Gets the configured video track name used for publishing.
        /// </summary>
        public string VideoTrackName => videoTrackName;

        private static async Task UnpublishAndDisposeAsync(IVideoTrackManager manager)
        {
            if (manager == null) return;

            try
            {
                await manager.UnpublishVideoAsync();
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiVideoPublisher] Error unpublishing on destroy: {ex.Message}",
                    LogCategory.Vision);
            }
            finally
            {
                try
                {
                    manager.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }

        /// <summary>
        ///     Injects the room connection service via the ConvaiManager pipeline.
        /// </summary>
        /// <param name="connectionService">Resolved room connection service (required).</param>
        public void Inject(IConvaiRoomConnectionService connectionService)
        {
            if (connectionService == null) throw new ArgumentNullException(nameof(connectionService));

            if (_roomConnectionService == connectionService) return;

            UnregisterConnectionCallbacks();
            _roomConnectionService = connectionService;
            _isInjected = true;

            if (!isActiveAndEnabled) return;

            RegisterConnectionCallbacks();

            if (_roomConnectionService.IsConnected) OnRoomConnected();
        }

        private void OnRoomConnected() => _ = HandleRoomConnectedAsync();

        private async Task HandleRoomConnectedAsync()
        {
            try
            {
                if (_roomConnectionService == null)
                {
                    ConvaiLogger.Error("Room connection service lost; cannot publish video.", LogCategory.Vision);
                    return;
                }

                IRoomFacade roomFacade = _roomConnectionService.CurrentRoom;
                if (roomFacade != null)
                {
                    ConvaiLogger.Info($"[ConvaiVideoPublisher] Unity player joined room: {roomFacade.Name}",
                        LogCategory.Vision);
                }

                await PublishVideoTrackAsync();
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiVideoPublisher] Error while handling room connected: {ex.Message}",
                    LogCategory.Vision);
            }
        }

        private void RegisterConnectionCallbacks()
        {
            if (_roomConnectionService == null || _connectionCallbacksRegistered) return;

            _roomConnectionService.Connected += OnRoomConnected;
            _connectionCallbacksRegistered = true;
        }

        private void UnregisterConnectionCallbacks()
        {
            if (!_connectionCallbacksRegistered || _roomConnectionService == null) return;

            _roomConnectionService.Connected -= OnRoomConnected;
            _connectionCallbacksRegistered = false;
        }

        private void ResolveFrameSource()
        {
            if (_frameSourceComponent != null)
            {
                FrameSource = _frameSourceComponent as IVisionFrameSource;
                if (FrameSource == null)
                {
                    ConvaiLogger.Warning(
                        $"[ConvaiVideoPublisher] Assigned component '{_frameSourceComponent.GetType().Name}' does not implement IVisionFrameSource",
                        LogCategory.Vision);
                }
            }

            if (FrameSource == null)
            {
                var vcs = GetComponent<CameraVisionFrameSource>();
                if (vcs != null)
                {
                    FrameSource = vcs;
                    _frameSourceComponent = vcs;
                }
            }

            if (FrameSource == null)
            {
                MonoBehaviour[] allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (MonoBehaviour mb in allBehaviours)
                {
                    if (mb is IVisionFrameSource source)
                    {
                        FrameSource = source;
                        _frameSourceComponent = mb;
                        break;
                    }
                }
            }

            if (FrameSource != null)
            {
                ConvaiLogger.Info($"[ConvaiVideoPublisher] Using frame source: {_frameSourceComponent.GetType().Name}",
                    LogCategory.Vision);
            }
        }

        /// <summary>
        ///     Publishes the video track to the LiveKit room using the VideoTrackManager.
        /// </summary>
        private async Task PublishVideoTrackAsync()
        {
            try
            {
                if (UsesWebGLCanvasPublishPath())
                {
                    await PublishWebGLCanvasTrackAsync();
                    return;
                }

                if (FrameSource == null)
                {
                    ConvaiLogger.Error("[ConvaiVideoPublisher] Frame source is null, cannot publish video",
                        LogCategory.Vision);
                    return;
                }

                if (!FrameSource.IsCapturing)
                {
                    FrameSource.StartCapture();
                    ConvaiLogger.Info("[ConvaiVideoPublisher] Started frame capture before publishing",
                        LogCategory.Vision);
                }

                const int maxWaitMs = 5000;
                const int pollIntervalMs = 100;
                int elapsed = 0;

                while (FrameSource.CurrentRenderTexture == null && elapsed < maxWaitMs)
                {
                    await Task.Delay(pollIntervalMs);
                    elapsed += pollIntervalMs;
                }

                RenderTexture renderTexture = FrameSource.CurrentRenderTexture;
                if (renderTexture == null)
                {
                    ConvaiLogger.Error(
                        $"[ConvaiVideoPublisher] Frame source RenderTexture not available after {maxWaitMs}ms timeout",
                        LogCategory.Vision);
                    return;
                }

                ConvaiLogger.Info($"[ConvaiVideoPublisher] Frame source ready after {elapsed}ms", LogCategory.Vision);

                IRoomFacade roomFacade = _roomConnectionService.CurrentRoom;
                if (roomFacade == null)
                {
                    ConvaiLogger.Error("[ConvaiVideoPublisher] Room is null, cannot publish video", LogCategory.Vision);
                    return;
                }

                if (_videoTrackManager == null)
                {
                    if (_eventHub == null)
                    {
                        ConvaiLogger.Error(
                            "[ConvaiVideoPublisher] EventHub not injected. Ensure ConvaiManager is in the scene.",
                            LogCategory.Vision);
                        return;
                    }

                    if (_videoSourceFactory == null)
                    {
                        ConvaiLogger.Error(
                            "[ConvaiVideoPublisher] VideoSourceFactory not injected. Ensure ConvaiManager is in the scene.",
                            LogCategory.Vision);
                        return;
                    }

                    _videoTrackManager = new VideoTrackManager(
                        () => _roomConnectionService.CurrentRoom,
                        _eventHub,
                        null,
                        _videoSourceFactory);
                }

                VideoPublishOptions options = VideoPublishOptions.Default
                    .WithTrackName(videoTrackName)
                    .WithMaxFrameRate(frameRate)
                    .WithMaxBitrate(maxBitrate);

                bool success = await _videoTrackManager.PublishVideoAsync(renderTexture, options);

                if (success)
                {
                    ConvaiLogger.Info($"[ConvaiVideoPublisher] Video track '{videoTrackName}' published successfully",
                        LogCategory.Vision);
                }
                else
                {
                    ConvaiLogger.Error($"[ConvaiVideoPublisher] Failed to publish video track '{videoTrackName}'",
                        LogCategory.Vision);
                }
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiVideoPublisher] Error publishing video track: {ex.Message}",
                    LogCategory.Vision);
            }
        }

        private async Task PublishWebGLCanvasTrackAsync()
        {
            if (_roomConnectionService?.CurrentRoom == null)
            {
                ConvaiLogger.Error("[ConvaiVideoPublisher] Room is null, cannot publish WebGL canvas video.",
                    LogCategory.Vision);
                return;
            }

            if (_videoSourceFactory == null)
            {
                ConvaiLogger.Error(
                    "[ConvaiVideoPublisher] VideoSourceFactory not injected. Ensure ConvaiManager is in the scene.",
                    LogCategory.Vision);
                return;
            }

            EnsureVideoTrackManager();

            IVideoSource canvasSource;
            try
            {
                canvasSource = _videoSourceFactory.CreateFromCanvasCapture(videoTrackName, frameRate);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiVideoPublisher] WebGL canvas capture is unavailable: {ex.Message}",
                    LogCategory.Vision);
                return;
            }

            VideoPublishOptions options = VideoPublishOptions.Default
                .WithTrackName(videoTrackName)
                .WithSource(VideoTrackSource.ScreenShare)
                .WithMaxFrameRate(frameRate)
                .WithMaxBitrate(maxBitrate);

            bool success = await _videoTrackManager.PublishVideoAsync(canvasSource, options);

            if (success)
            {
                ConvaiLogger.Info(
                    $"[ConvaiVideoPublisher] WebGL canvas track '{videoTrackName}' published successfully",
                    LogCategory.Vision);
            }
            else
            {
                ConvaiLogger.Error($"[ConvaiVideoPublisher] Failed to publish WebGL canvas track '{videoTrackName}'",
                    LogCategory.Vision);
            }
        }

        private void EnsureVideoTrackManager()
        {
            if (_videoTrackManager != null) return;

            if (_eventHub == null)
                throw new InvalidOperationException("EventHub not injected. Ensure ConvaiManager is in the scene.");

            if (_videoSourceFactory == null)
            {
                throw new InvalidOperationException(
                    "VideoSourceFactory not injected. Ensure ConvaiManager is in the scene.");
            }

            _videoTrackManager = new VideoTrackManager(
                () => _roomConnectionService.CurrentRoom,
                _eventHub,
                null,
                _videoSourceFactory);
        }

        private static bool UsesWebGLCanvasPublishPath() =>
            UsesWebGLCanvasPublishPath(UnityEngine.Application.platform);

        private static bool UsesWebGLCanvasPublishPath(RuntimePlatform platform) =>
            platform == RuntimePlatform.WebGLPlayer;

        /// <summary>
        ///     Manually unpublishes the video track.
        /// </summary>
        public async Task UnpublishVideoAsync()
        {
            if (_videoTrackManager == null) return;

            try
            {
                await _videoTrackManager.UnpublishVideoAsync();
                ConvaiLogger.Info("[ConvaiVideoPublisher] Video track unpublished", LogCategory.Vision);
            }
            catch (Exception ex)
            {
                ConvaiLogger.Error($"[ConvaiVideoPublisher] Error unpublishing video track: {ex.Message}",
                    LogCategory.Vision);
            }
        }
    }
}
