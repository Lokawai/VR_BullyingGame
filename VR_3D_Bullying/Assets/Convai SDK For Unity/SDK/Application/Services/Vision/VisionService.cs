using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Vision;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Application.Services.Vision
{
    /// <summary>
    ///     Application-layer service that manages vision state transitions.
    ///     Publishes domain events for state changes.
    /// </summary>
    /// <remarks>
    ///     This service:
    ///     - Manages vision state transitions
    ///     - Publishes domain events for state changes
    ///     - Coordinates with video track manager for unpublishing
    ///     Note: Video capture and publishing is handled by Unity layer components
    ///     (CameraVisionFrameSource, ConvaiVideoPublisher) that implement IVisionFrameSource.
    /// </remarks>
    public class VisionService : IVisionService, IDisposable
    {
        private readonly IEventHub _eventHub;
        private readonly ILogger _logger;
        private readonly object _stateLock = new();
        private VisionCaptureSettings? _currentSettings;
        private bool _disposed;

        private VisionState _state = VisionState.Disabled;
        private IVideoTrackUnpublisher _videoUnpublisher;

        /// <summary>
        ///     Initializes a new instance of the <see cref="VisionService" /> class.
        /// </summary>
        /// <param name="videoUnpublisher">
        ///     Video track unpublisher for stopping video streams (can be null, set later via
        ///     SetVideoUnpublisher).
        /// </param>
        /// <param name="eventHub">Event hub for publishing domain events.</param>
        /// <param name="logger">Logger for diagnostic messages (optional).</param>
        public VisionService(
            IVideoTrackUnpublisher videoUnpublisher,
            IEventHub eventHub,
            ILogger logger = null)
        {
            _videoUnpublisher = videoUnpublisher;
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _logger = logger;
        }

        /// <summary>
        ///     Disposes the vision service and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            if (State != VisionState.Disabled) State = VisionState.Disabled;

            _logger?.Info("[VisionService] Disposed");
        }

        /// <inheritdoc />
        public VisionState State
        {
            get
            {
                lock (_stateLock) return _state;
            }
            private set
            {
                lock (_stateLock) _state = value;
            }
        }

        /// <inheritdoc />
        public bool IsEnabled => State != VisionState.Disabled && State != VisionState.Error;

        /// <inheritdoc />
        public VisionCaptureSettings? CurrentSettings
        {
            get
            {
                lock (_stateLock) return _currentSettings;
            }
        }

        /// <inheritdoc />
        public Task<bool> EnableAsync(CancellationToken ct = default) => EnableAsync(VisionCaptureSettings.Default, ct);

        /// <inheritdoc />
        public async Task<bool> EnableAsync(VisionCaptureSettings settings, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (State == VisionState.Publishing || State == VisionState.Capturing)
            {
                _logger?.Info("[VisionService] Already enabled, ignoring EnableAsync call");
                return true;
            }

            try
            {
                ct.ThrowIfCancellationRequested();

                State = VisionState.Initializing;
                _currentSettings = settings;

                _logger?.Info(
                    $"[VisionService] Enabling vision: {settings.Width}x{settings.Height} @ {settings.FrameRate}fps");

                PublishCaptureStartedEvent(settings);

                State = VisionState.Publishing;
                _logger?.Info("[VisionService] Vision enabled successfully");

                await Task.CompletedTask.ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger?.Info("[VisionService] EnableAsync was cancelled");
                State = VisionState.Disabled;
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Error($"[VisionService] Error enabling vision: {ex}");
                State = VisionState.Error;
                return false;
            }
        }

        /// <inheritdoc />
        public async Task DisableAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (State == VisionState.Disabled) return;

            try
            {
                _logger?.Info("[VisionService] Disabling vision");

                if (_videoUnpublisher != null) await _videoUnpublisher.UnpublishVideoAsync(ct).ConfigureAwait(false);

                PublishCaptureStoppedEvent(VisionCaptureStopReason.UserRequested, 0);

                State = VisionState.Disabled;
                _currentSettings = null;
                _logger?.Info("[VisionService] Vision disabled successfully");
            }
            catch (OperationCanceledException)
            {
                _logger?.Info("[VisionService] DisableAsync was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Error($"[VisionService] Error disabling vision: {ex}");
                State = VisionState.Error;
            }
        }

        /// <summary>
        ///     Sets the video track unpublisher. Called when room connects and VideoTrackManager is created.
        /// </summary>
        /// <param name="unpublisher">The video track unpublisher instance.</param>
        public void SetVideoUnpublisher(IVideoTrackUnpublisher unpublisher)
        {
            ThrowIfDisposed();
            _videoUnpublisher = unpublisher;
            _logger?.Info("[VisionService] Video track unpublisher set");
        }

        private void PublishCaptureStartedEvent(VisionCaptureSettings settings)
        {
            try
            {
                var domainEvent = VisionCaptureStarted.Create(
                    settings.Width,
                    settings.Height,
                    settings.FrameRate
                );
                _eventHub.Publish(domainEvent);
            }
            catch (Exception ex)
            {
                _logger?.Error($"[VisionService] Failed to publish VisionCaptureStarted event: {ex}");
            }
        }

        private void PublishCaptureStoppedEvent(VisionCaptureStopReason reason, long totalFramesCaptured)
        {
            try
            {
                var domainEvent = VisionCaptureStopped.Create(
                    totalFramesCaptured,
                    reason
                );
                _eventHub.Publish(domainEvent);
            }
            catch (Exception ex)
            {
                _logger?.Error($"[VisionService] Failed to publish VisionCaptureStopped event: {ex}");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VisionService));
        }
    }
}
