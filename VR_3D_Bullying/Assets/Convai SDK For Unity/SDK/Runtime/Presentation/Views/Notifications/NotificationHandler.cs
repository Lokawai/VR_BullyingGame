using System.Collections;
using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Shared;
using Convai.Shared.Abstractions;
using Convai.Shared.DependencyInjection;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Presentation.Views.Notifications
{
    /// <summary>
    ///     Handles the notification system's behavior and interactions.
    /// </summary>
    public class NotificationHandler : MonoBehaviour, IInjectable
    {
        /// <summary>
        ///     Array containing predefined notification configurations.
        ///     This array can be modified in the Unity Editor to define different types of notifications.
        /// </summary>
        [SerializeField] private SONotificationGroup notificationGroup;

        [SerializeField] private UINotificationController notificationControllerPrefab;
        private readonly Dictionary<NotificationType, SONotification> _notificationLookup = new();
        private IServiceContainer _container;
        private Coroutine _dependencyWaitRoutine;
        private bool _eventsSubscribed;
        private IConvaiNotificationService _notificationService;
        private IConvaiRuntimeSettingsService _runtimeSettingsService;

        private UINotificationController _spawnedController;

        private void Awake()
        {
            ResolveNotificationGroup();
            BuildNotificationLookup();

            // Try to find existing controller in children first
            _spawnedController = GetComponentInChildren<UINotificationController>(true);

            // Only instantiate if no existing controller and prefab is set
            if (_spawnedController == null && notificationControllerPrefab != null)
                _spawnedController = Instantiate(notificationControllerPrefab, transform);
            else if (_spawnedController == null)
            {
                ConvaiLogger.Warning("[NotificationHandler] No UINotificationController found and no prefab set.",
                    LogCategory.UI);
            }
        }

        /// <summary>
        ///     This function is called when the object becomes enabled and active.
        ///     It is used to subscribe to the OnNotificationRequested event.
        /// </summary>
        private void OnEnable()
        {
            if (!TryInitializeNotificationService())
            {
                if (_dependencyWaitRoutine == null)
                    _dependencyWaitRoutine = StartCoroutine(WaitForNotificationService());

                ConvaiLogger.Warning(
                    "[NotificationHandler] Notification service not available; notifications will be deferred until services initialize.",
                    LogCategory.UI);
            }
        }

        /// <summary>
        ///     This function is called when the behaviour becomes disabled or inactive.
        ///     It is used to unsubscribe from the OnNotificationRequested event.
        /// </summary>
        private void OnDisable()
        {
            if (_notificationService != null)
            {
                _notificationService.OnNotificationRequested -= NotificationRequest;
                _notificationService.OnNotificationDismissed -= OnNotificationDismissed;
                _eventsSubscribed = false;
            }

            if (_dependencyWaitRoutine != null)
            {
                StopCoroutine(_dependencyWaitRoutine);
                _dependencyWaitRoutine = null;
            }
        }

        /// <inheritdoc />
        public void InjectServices(IServiceContainer container)
        {
            _container = container;
            container.TryGet(out IConvaiNotificationService notificationService);
            container.TryGet(out IConvaiRuntimeSettingsService runtimeSettings);
            Inject(notificationService, runtimeSettings);
        }

        public void Inject(
            IConvaiNotificationService notificationService = null,
            IConvaiRuntimeSettingsService runtimeSettingsService = null)
        {
            _notificationService = notificationService;
            _runtimeSettingsService = runtimeSettingsService;
        }

        /// <summary>
        ///     Requests a notification of the specified type.
        /// </summary>
        /// <param name="notificationType">The type of notification to request.</param>
        private void NotificationRequest(NotificationType notificationType)
        {
            if (_runtimeSettingsService == null || !_runtimeSettingsService.Current.NotificationsEnabled)
            {
                ConvaiLogger.Info("Cannot send notification because notifications are disabled in runtime settings.",
                    LogCategory.UI);
                return;
            }

            if (!_notificationLookup.TryGetValue(notificationType, out SONotification requestedNotification) ||
                requestedNotification == null)
            {
                ConvaiLogger.Warning($"[NotificationHandler] No notification defined for type: {notificationType}",
                    LogCategory.UI);
                return;
            }

            if (_spawnedController != null)
                _spawnedController.Notify(requestedNotification);
            else
            {
                ConvaiLogger.Error(
                    "[NotificationHandler] UINotificationController is null, cannot display notification.",
                    LogCategory.UI);
            }
        }

        private void ResolveNotificationGroup()
        {
            if (notificationGroup != null) return;

            if (!SONotificationGroup.GetGroup(out notificationGroup))
            {
                ConvaiLogger.Error("[NotificationHandler] SONotificationGroup asset could not be resolved.",
                    LogCategory.UI);
            }
        }

        private void BuildNotificationLookup()
        {
            _notificationLookup.Clear();

            if (notificationGroup?.soNotifications == null)
            {
                ConvaiLogger.Error("[NotificationHandler] Notification group or soNotifications is null!",
                    LogCategory.UI);
                return;
            }

            foreach (SONotification notification in notificationGroup.soNotifications)
            {
                if (notification == null) continue;

                NotificationType type = notification.notificationType;
                if (_notificationLookup.ContainsKey(type))
                {
                    ConvaiLogger.Warning(
                        $"[NotificationHandler] Duplicate notification configured for type: {type}. Keeping the first entry.",
                        LogCategory.UI);
                    continue;
                }

                _notificationLookup.Add(type, notification);
            }
        }

        private void OnNotificationDismissed()
        {
            if (_spawnedController != null) _spawnedController.ClearAll();
        }

        private void EnsureNotificationService()
        {
            if (_notificationService != null) return;

            _container?.TryGet(out _notificationService);
        }

        private void EnsureRuntimeSettingsService()
        {
            if (_runtimeSettingsService != null) return;

            _container?.TryGet(out _runtimeSettingsService);
        }

        private bool TryInitializeNotificationService()
        {
            EnsureNotificationService();
            EnsureRuntimeSettingsService();

            if (_notificationService == null || _runtimeSettingsService == null) return false;

            if (!_eventsSubscribed)
            {
                _notificationService.OnNotificationRequested += NotificationRequest;
                _notificationService.OnNotificationDismissed += OnNotificationDismissed;
                _eventsSubscribed = true;
            }

            return true;
        }

        private IEnumerator WaitForNotificationService()
        {
            IConvaiNotificationService resolvedService = null;
            IConvaiRuntimeSettingsService resolvedRuntimeSettings = null;
            yield return new WaitUntil(() =>
                _container != null &&
                _container.TryGet(out resolvedService) &&
                _container.TryGet(out resolvedRuntimeSettings));

            _notificationService = resolvedService;
            _runtimeSettingsService = resolvedRuntimeSettings;

            if (!TryInitializeNotificationService())
            {
                ConvaiLogger.Warning(
                    "[NotificationHandler] Notification service still unavailable after service locator initialization.",
                    LogCategory.UI);
            }

            _dependencyWaitRoutine = null;
        }
    }
}
