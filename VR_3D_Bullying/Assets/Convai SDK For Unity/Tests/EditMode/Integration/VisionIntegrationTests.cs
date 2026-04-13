using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Convai.Application.Services.Vision;
using Convai.Domain.DomainEvents.Vision;
using Convai.Domain.EventSystem;
using Convai.Domain.Models;
using NUnit.Framework;

namespace Convai.Tests.EditMode
{
    /// <summary>
    ///     Integration tests for the Vision module.
    ///     Tests VisionService lifecycle and EventHub integration.
    /// </summary>
    /// <remarks>
    ///     Note: VisionService is now a lightweight state manager. Actual video capture
    ///     is handled by Unity layer components (CameraVisionFrameSource, WebcamVisionFrameSource)
    ///     that implement IVisionFrameSource. Video publishing is handled by ConvaiVideoPublisher.
    /// </remarks>
    [Category("Integration")]
    public class VisionIntegrationTests
    {
        private IEventHub _eventHub;
        private List<VisionFrameCaptured> _frameCapturedEvents;
        private List<VisionCaptureStarted> _startedEvents;
        private List<VisionCaptureStopped> _stoppedEvents;
        private VisionService _visionService;

        [SetUp]
        public void SetUp()
        {
            _eventHub = new EventHub(new ImmediateScheduler());
            _visionService = new VisionService(null, _eventHub);

            _startedEvents = new List<VisionCaptureStarted>();
            _frameCapturedEvents = new List<VisionFrameCaptured>();
            _stoppedEvents = new List<VisionCaptureStopped>();

            _eventHub.Subscribe<VisionCaptureStarted>(e => _startedEvents.Add(e));
            _eventHub.Subscribe<VisionFrameCaptured>(e => _frameCapturedEvents.Add(e));
            _eventHub.Subscribe<VisionCaptureStopped>(e => _stoppedEvents.Add(e));
        }

        [TearDown]
        public void TearDown()
        {
            _visionService?.Dispose();
            _visionService = null;
            _eventHub = null;
            _startedEvents = null;
            _frameCapturedEvents = null;
            _stoppedEvents = null;
        }

        #region Test Infrastructure

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        #endregion

        #region VisionService Lifecycle Tests

        [Test]
        public void VisionService_InitialState_IsDisabled()
        {
            Assert.AreEqual(VisionState.Disabled, _visionService.State);
            Assert.IsFalse(_visionService.IsEnabled);
            Assert.IsNull(_visionService.CurrentSettings);
        }

        [Test]
        public void VisionService_EnableAsync_TransitionsToPublishing()
        {
            Task<bool> task = _visionService.EnableAsync();
            task.Wait();

            Assert.IsTrue(task.Result);
            Assert.AreEqual(VisionState.Publishing, _visionService.State);
            Assert.IsTrue(_visionService.IsEnabled);
        }

        [Test]
        public void VisionService_EnableAsync_PublishesCaptureStartedEvent()
        {
            _visionService.EnableAsync().Wait();

            Assert.AreEqual(1, _startedEvents.Count);
        }

        [Test]
        public void VisionService_DisableAsync_TransitionsToDisabled()
        {
            _visionService.EnableAsync().Wait();
            _visionService.DisableAsync().Wait();

            Assert.AreEqual(VisionState.Disabled, _visionService.State);
            Assert.IsFalse(_visionService.IsEnabled);
        }

        [Test]
        public void VisionService_DisableAsync_PublishesCaptureStoppedEvent()
        {
            _visionService.EnableAsync().Wait();
            _visionService.DisableAsync().Wait();

            Assert.AreEqual(1, _stoppedEvents.Count);
            Assert.AreEqual(VisionCaptureStopReason.UserRequested, _stoppedEvents[0].Reason);
        }

        [Test]
        public void VisionService_EnableAsync_WhenAlreadyEnabled_ReturnsTrue()
        {
            _visionService.EnableAsync().Wait();
            Task<bool> secondEnable = _visionService.EnableAsync();
            secondEnable.Wait();

            Assert.IsTrue(secondEnable.Result);
            Assert.AreEqual(1, _startedEvents.Count);
        }

        [Test]
        public void VisionService_DisableAsync_WhenAlreadyDisabled_DoesNothing()
        {
            _visionService.DisableAsync().Wait();

            Assert.AreEqual(VisionState.Disabled, _visionService.State);
            Assert.AreEqual(0, _stoppedEvents.Count);
        }

        [Test]
        public void VisionService_Dispose_StopsCapture()
        {
            _visionService.EnableAsync().Wait();
            _visionService.Dispose();

            Assert.AreEqual(VisionState.Disabled, _visionService.State);
        }

        [Test]
        public void VisionService_AfterDispose_ThrowsObjectDisposedException()
        {
            _visionService.Dispose();

            var ex = Assert.Throws<AggregateException>(() => _visionService.EnableAsync().Wait());
            Assert.IsInstanceOf<ObjectDisposedException>(ex.InnerException);
        }

        #endregion

        #region Custom Settings Tests

        [Test]
        public void VisionService_EnableAsync_WithCustomSettings_StoresSettings()
        {
            var settings = new VisionCaptureSettings(1280, 720, 15, 90, null);
            _visionService.EnableAsync(settings).Wait();

            Assert.IsNotNull(_visionService.CurrentSettings);
            Assert.AreEqual(1280, _visionService.CurrentSettings.Value.Width);
            Assert.AreEqual(720, _visionService.CurrentSettings.Value.Height);
            Assert.AreEqual(15, _visionService.CurrentSettings.Value.FrameRate);
            Assert.AreEqual(90, _visionService.CurrentSettings.Value.JpegQuality);
        }

        [Test]
        public void VisionService_DisableAsync_ClearsSettings()
        {
            var settings = new VisionCaptureSettings(1280, 720, 15, 90, null);
            _visionService.EnableAsync(settings).Wait();
            _visionService.DisableAsync().Wait();

            Assert.IsNull(_visionService.CurrentSettings);
        }

        #endregion

        #region EventHub Integration Tests

        [Test]
        public void EventHub_VisionCaptureStarted_IncludesCorrectDimensions()
        {
            var settings = new VisionCaptureSettings(800, 600, 24, 80, null);
            _visionService.EnableAsync(settings).Wait();

            Assert.AreEqual(1, _startedEvents.Count);
            Assert.AreEqual(800, _startedEvents[0].Width);
            Assert.AreEqual(600, _startedEvents[0].Height);
            Assert.AreEqual(24, _startedEvents[0].FramesPerSecond);
        }

        [Test]
        public void EventHub_VisionCaptureStopped_IncludesReason()
        {
            _visionService.EnableAsync().Wait();
            _visionService.DisableAsync().Wait();

            Assert.AreEqual(1, _stoppedEvents.Count);
            Assert.AreEqual(VisionCaptureStopReason.UserRequested, _stoppedEvents[0].Reason);
            Assert.IsNull(_stoppedEvents[0].ErrorMessage);
        }

        #endregion
    }
}
