using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Runtime.Presentation.Services;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Presentation
{
    /// <summary>
    ///     Tests for the notification system components:
    ///     - ConvaiNotificationService
    ///     - ConvaiNotificationEventBridge
    /// </summary>
    public class NotificationSystemTests
    {
        #region Helper Methods

        private (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub)
            CreateEventBridge(bool notificationsEnabled)
        {
            var mockService = new MockNotificationService();
            var eventHub = new EventHub(new ImmediateScheduler());
            var bridge = new ConvaiNotificationEventBridge(mockService, eventHub, () => notificationsEnabled);

            return (bridge, mockService, eventHub);
        }

        #endregion

        #region ImmediateScheduler

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        #endregion

        #region Mock NotificationService

        private sealed class MockNotificationService : IConvaiNotificationService
        {
            public List<NotificationType> RequestedNotifications { get; } = new();
            public int DismissCount { get; private set; }

            public event Action<NotificationType> OnNotificationRequested;
            public event Action OnNotificationDismissed;

            public void RequestNotification(NotificationType notificationType)
            {
                RequestedNotifications.Add(notificationType);
                OnNotificationRequested?.Invoke(notificationType);
            }

            public void DismissNotification()
            {
                DismissCount++;
                OnNotificationDismissed?.Invoke();
            }
        }

        #endregion

        #region ConvaiNotificationService Tests

        [Test]
        public void ConvaiNotificationService_RequestNotification_InvokesHandler()
        {
            var scheduler = new ImmediateScheduler();
            var service = new ConvaiNotificationService(scheduler);
            NotificationType? receivedType = null;

            service.OnNotificationRequested += type => receivedType = type;
            service.RequestNotification(NotificationType.MICROPHONE_ISSUE);

            Assert.AreEqual(NotificationType.MICROPHONE_ISSUE, receivedType);
        }

        [Test]
        public void ConvaiNotificationService_RequestNotification_NoHandler_DoesNotThrow()
        {
            var scheduler = new ImmediateScheduler();
            var service = new ConvaiNotificationService(scheduler);

            Assert.DoesNotThrow(() => service.RequestNotification(NotificationType.MICROPHONE_ISSUE));
        }

        [Test]
        public void ConvaiNotificationService_DismissNotification_InvokesHandler()
        {
            var scheduler = new ImmediateScheduler();
            var service = new ConvaiNotificationService(scheduler);
            bool dismissed = false;

            service.OnNotificationDismissed += () => dismissed = true;
            service.DismissNotification();

            Assert.IsTrue(dismissed);
        }

        [Test]
        public void ConvaiNotificationService_MultipleHandlers_AllInvoked()
        {
            var scheduler = new ImmediateScheduler();
            var service = new ConvaiNotificationService(scheduler);
            int invokeCount = 0;

            service.OnNotificationRequested += _ => invokeCount++;
            service.OnNotificationRequested += _ => invokeCount++;

            service.RequestNotification(NotificationType.NETWORK_REACHABILITY_ISSUE);

            Assert.AreEqual(2, invokeCount);
        }

        [Test]
        public void ConvaiNotificationService_UnsubscribeHandler_NoLongerInvoked()
        {
            var scheduler = new ImmediateScheduler();
            var service = new ConvaiNotificationService(scheduler);
            int invokeCount = 0;

            void Handler(NotificationType type) => invokeCount++;

            service.OnNotificationRequested += Handler;
            service.RequestNotification(NotificationType.MICROPHONE_ISSUE);
            Assert.AreEqual(1, invokeCount);

            service.OnNotificationRequested -= Handler;
            service.RequestNotification(NotificationType.MICROPHONE_ISSUE);
            Assert.AreEqual(1, invokeCount, "Handler should not be invoked after unsubscribe");
        }

        #endregion

        #region ConvaiNotificationEventBridge Error Code Mapping Tests

        [Test]
        public void EventBridge_AuthFailed_MapsToApiKeyNotFound()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);

            eventHub.Publish(SessionError.Create(SessionErrorCodes.ConnectionAuthFailed, "Auth failed",
                "test-session"));

            Assert.AreEqual(1, mockService.RequestedNotifications.Count);
            Assert.AreEqual(NotificationType.API_KEY_NOT_FOUND, mockService.RequestedNotifications[0]);

            bridge.Dispose();
        }

        [Test]
        public void EventBridge_RateLimited_MapsToUsageLimitExceeded()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);

            eventHub.Publish(SessionError.Create(SessionErrorCodes.ConnectionRateLimited, "Rate limited",
                "test-session"));

            Assert.AreEqual(1, mockService.RequestedNotifications.Count);
            Assert.AreEqual(NotificationType.USAGE_LIMIT_EXCEEDED, mockService.RequestedNotifications[0]);

            bridge.Dispose();
        }

        [Test]
        public void EventBridge_NetworkError_MapsToNetworkReachabilityIssue()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);

            eventHub.Publish(SessionError.Create(SessionErrorCodes.ConnectionNetworkError, "Network error",
                "test-session"));

            Assert.AreEqual(1, mockService.RequestedNotifications.Count);
            Assert.AreEqual(NotificationType.NETWORK_REACHABILITY_ISSUE, mockService.RequestedNotifications[0]);

            bridge.Dispose();
        }

        [Test]
        public void EventBridge_Timeout_MapsToNetworkReachabilityIssue()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);

            eventHub.Publish(SessionError.Create(SessionErrorCodes.ConnectionTimeout, "Timeout", "test-session"));

            Assert.AreEqual(1, mockService.RequestedNotifications.Count);
            Assert.AreEqual(NotificationType.NETWORK_REACHABILITY_ISSUE, mockService.RequestedNotifications[0]);

            bridge.Dispose();
        }

        [Test]
        public void EventBridge_MicUnavailable_MapsToNoMicrophoneDetected()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);

            eventHub.Publish(SessionError.Create(SessionErrorCodes.AudioMicUnavailable, "No mic", "test-session"));

            Assert.AreEqual(1, mockService.RequestedNotifications.Count);
            Assert.AreEqual(NotificationType.NO_MICROPHONE_DETECTED, mockService.RequestedNotifications[0]);

            bridge.Dispose();
        }

        [Test]
        public void EventBridge_MicPermissionDenied_MapsToMicrophoneIssue()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);

            eventHub.Publish(SessionError.Create(SessionErrorCodes.AudioMicPermissionDenied, "Permission denied",
                "test-session"));

            Assert.AreEqual(1, mockService.RequestedNotifications.Count);
            Assert.AreEqual(NotificationType.MICROPHONE_ISSUE, mockService.RequestedNotifications[0]);

            bridge.Dispose();
        }

        [Test]
        public void EventBridge_UnknownErrorCode_WithAudioPrefix_MapsToMicrophoneIssue()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);

            eventHub.Publish(SessionError.Create("audio.unknown_error", "Unknown audio error", "test-session"));

            Assert.AreEqual(1, mockService.RequestedNotifications.Count);
            Assert.AreEqual(NotificationType.MICROPHONE_ISSUE, mockService.RequestedNotifications[0]);

            bridge.Dispose();
        }

        [Test]
        public void EventBridge_UnknownErrorCode_WithConnectionPrefix_MapsToNetworkIssue()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);

            eventHub.Publish(SessionError.Create("connection.unknown", "Unknown connection error", "test-session"));

            Assert.AreEqual(1, mockService.RequestedNotifications.Count);
            Assert.AreEqual(NotificationType.NETWORK_REACHABILITY_ISSUE, mockService.RequestedNotifications[0]);

            bridge.Dispose();
        }

        [Test]
        public void EventBridge_CompletelyUnknownErrorCode_DoesNotNotify()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);

            eventHub.Publish(SessionError.Create("completely.unknown.code", "Unknown error", "test-session"));

            Assert.AreEqual(0, mockService.RequestedNotifications.Count);

            bridge.Dispose();
        }

        #endregion

        #region Cooldown Tests

        [Test]
        public void EventBridge_Cooldown_PreventsDuplicateNotifications()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);
            bridge.CooldownSeconds = 60f; // Long cooldown

            // First notification should go through
            eventHub.Publish(SessionError.Create(SessionErrorCodes.ConnectionNetworkError, "Error 1", "test-session"));
            Assert.AreEqual(1, mockService.RequestedNotifications.Count);

            // Same type within cooldown should be suppressed
            eventHub.Publish(SessionError.Create(SessionErrorCodes.ConnectionTimeout, "Error 2", "test-session"));
            Assert.AreEqual(1, mockService.RequestedNotifications.Count,
                "Same notification type should be suppressed during cooldown");

            bridge.Dispose();
        }

        [Test]
        public void EventBridge_DifferentNotificationTypes_NotAffectedByCooldown()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);
            bridge.CooldownSeconds = 60f;

            eventHub.Publish(SessionError.Create(SessionErrorCodes.ConnectionNetworkError, "Network error",
                "test-session"));
            eventHub.Publish(SessionError.Create(SessionErrorCodes.AudioMicUnavailable, "Mic error", "test-session"));

            Assert.AreEqual(2, mockService.RequestedNotifications.Count,
                "Different notification types should not affect each other's cooldown");
            Assert.AreEqual(NotificationType.NETWORK_REACHABILITY_ISSUE, mockService.RequestedNotifications[0]);
            Assert.AreEqual(NotificationType.NO_MICROPHONE_DETECTED, mockService.RequestedNotifications[1]);

            bridge.Dispose();
        }

        [Test]
        public void EventBridge_ClearCooldowns_AllowsImmediateNotification()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);
            bridge.CooldownSeconds = 60f;

            eventHub.Publish(SessionError.Create(SessionErrorCodes.ConnectionNetworkError, "Error 1", "test-session"));
            Assert.AreEqual(1, mockService.RequestedNotifications.Count);

            bridge.ClearCooldowns();

            eventHub.Publish(SessionError.Create(SessionErrorCodes.ConnectionNetworkError, "Error 2", "test-session"));
            Assert.AreEqual(2, mockService.RequestedNotifications.Count,
                "After clearing cooldowns, same notification type should work");

            bridge.Dispose();
        }

        #endregion

        #region NotificationsEnabled Tests

        [Test]
        public void EventBridge_NotificationsDisabled_DoesNotNotify()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(false);

            eventHub.Publish(SessionError.Create(SessionErrorCodes.ConnectionNetworkError, "Error", "test-session"));

            Assert.AreEqual(0, mockService.RequestedNotifications.Count,
                "Should not notify when notifications are disabled");

            bridge.Dispose();
        }

        [Test]
        public void EventBridge_RequestNotificationIfEnabled_RespectsSettings()
        {
            bool enabled = false;
            var mockService = new MockNotificationService();
            var eventHub = new EventHub(new ImmediateScheduler());
            var bridge = new ConvaiNotificationEventBridge(mockService, eventHub, () => enabled);

            // Disabled - should not notify
            bridge.RequestNotificationIfEnabled(NotificationType.MICROPHONE_ISSUE);
            Assert.AreEqual(0, mockService.RequestedNotifications.Count);

            // Enable and try again
            enabled = true;
            bridge.RequestNotificationIfEnabled(NotificationType.MICROPHONE_ISSUE);
            Assert.AreEqual(1, mockService.RequestedNotifications.Count);

            bridge.Dispose();
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void EventBridge_AfterDispose_DoesNotReceiveEvents()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService mockService, EventHub eventHub) =
                CreateEventBridge(true);

            bridge.Dispose();

            eventHub.Publish(SessionError.Create(SessionErrorCodes.ConnectionNetworkError, "Error", "test-session"));

            Assert.AreEqual(0, mockService.RequestedNotifications.Count, "Should not receive events after dispose");
        }

        [Test]
        public void EventBridge_DoubleDispose_DoesNotThrow()
        {
            (ConvaiNotificationEventBridge bridge, MockNotificationService _, EventHub _) = CreateEventBridge(true);

            Assert.DoesNotThrow(() =>
            {
                bridge.Dispose();
                bridge.Dispose();
            });
        }

        #endregion
    }
}
