using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Logging;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine;
using UnityEngine.Scripting;

namespace Convai.Runtime.Presentation.Services
{
    /// <summary>
    ///     Bridges domain events (SessionError, etc.) to notification requests.
    ///     Eagerly initialized by bootstrap to subscribe to domain events.
    /// </summary>
    /// <remarks>
    ///     This service:
    ///     - Subscribes to SessionError events from IEventHub
    ///     - Maps error codes to NotificationType values
    ///     - Checks runtime notification enablement before requesting notifications
    ///     - Implements cooldown/deduplication to avoid notification spam
    /// </remarks>
    [Preserve]
    public sealed class ConvaiNotificationEventBridge : IDisposable
    {
        private readonly object _cooldownLock = new();
        private readonly IEventHub _eventHub;
        private readonly Func<bool> _isNotificationEnabled;
        private readonly Dictionary<NotificationType, float> _lastShownTime = new();
        private readonly Func<IConvaiNotificationService> _notificationServiceAccessor;

        private SubscriptionToken _sessionErrorToken;

        /// <summary>
        ///     Creates a new ConvaiNotificationEventBridge.
        /// </summary>
        /// <param name="notificationService">The notification service to send requests to.</param>
        /// <param name="eventHub">The event hub to subscribe to domain events.</param>
        /// <param name="isNotificationEnabled">Function that returns true if notifications are enabled in settings.</param>
        public ConvaiNotificationEventBridge(
            IConvaiNotificationService notificationService,
            IEventHub eventHub,
            Func<bool> isNotificationEnabled)
        {
            if (notificationService == null) throw new ArgumentNullException(nameof(notificationService));

            _notificationServiceAccessor = () => notificationService;
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _isNotificationEnabled = isNotificationEnabled ?? (() => false);

            SubscribeToEvents();
        }

        /// <summary>
        ///     Creates a new ConvaiNotificationEventBridge with late-bound notification service resolution.
        /// </summary>
        /// <param name="notificationServiceAccessor">
        ///     Accessor that returns the current notification service instance (or null if
        ///     unavailable).
        /// </param>
        /// <param name="eventHub">The event hub to subscribe to domain events.</param>
        /// <param name="isNotificationEnabled">Function that returns true if notifications are enabled in settings.</param>
        public ConvaiNotificationEventBridge(
            Func<IConvaiNotificationService> notificationServiceAccessor,
            IEventHub eventHub,
            Func<bool> isNotificationEnabled)
        {
            _notificationServiceAccessor = notificationServiceAccessor ?? (() => null);
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
            _isNotificationEnabled = isNotificationEnabled ?? (() => false);

            SubscribeToEvents();
        }

        /// <summary>
        ///     Cooldown period in seconds before the same notification type can be shown again.
        /// </summary>
        public float CooldownSeconds { get; set; } = 10f;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_sessionErrorToken != default && _eventHub != null)
            {
                _eventHub.Unsubscribe(_sessionErrorToken);
                _sessionErrorToken = default;
            }

            ConvaiLogger.Debug("[ConvaiNotificationEventBridge] Disposed", LogCategory.SDK);
        }

        private void SubscribeToEvents()
        {
            _sessionErrorToken = _eventHub.Subscribe<SessionError>(
                HandleSessionError);

            ConvaiLogger.Debug("[ConvaiNotificationEventBridge] Subscribed to SessionError events", LogCategory.SDK);
        }

        private void HandleSessionError(SessionError error)
        {
            if (!_isNotificationEnabled()) return;

            IConvaiNotificationService notificationService = _notificationServiceAccessor?.Invoke();
            if (notificationService == null) return;

            NotificationType? notificationType = MapErrorCodeToNotificationType(error.ErrorCode);
            if (!notificationType.HasValue)
            {
                ConvaiLogger.Debug(
                    $"[ConvaiNotificationEventBridge] No notification mapping for error code: {error.ErrorCode}",
                    LogCategory.SDK);
                return;
            }

            if (!TryPassCooldown(notificationType.Value))
            {
                ConvaiLogger.Debug(
                    $"[ConvaiNotificationEventBridge] Notification {notificationType.Value} suppressed by cooldown",
                    LogCategory.SDK);
                return;
            }

            ConvaiLogger.Debug(
                $"[ConvaiNotificationEventBridge] Requesting notification: {notificationType.Value} for error: {error.ErrorCode}",
                LogCategory.SDK);
            notificationService.RequestNotification(notificationType.Value);
        }

        /// <summary>
        ///     Maps error codes to notification types.
        /// </summary>
        private static NotificationType? MapErrorCodeToNotificationType(string errorCode)
        {
            if (string.IsNullOrEmpty(errorCode)) return null;

            return errorCode switch
            {
                // Auth/API errors
                SessionErrorCodes.ConnectionAuthFailed => NotificationType.API_KEY_NOT_FOUND,
                SessionErrorCodes.ConnectionInvalidToken => NotificationType.API_KEY_NOT_FOUND,
                SessionErrorCodes.ConfigApiKeyMissing => NotificationType.API_KEY_NOT_FOUND,

                // Rate limiting
                SessionErrorCodes.ConnectionRateLimited => NotificationType.USAGE_LIMIT_EXCEEDED,

                // Network errors
                SessionErrorCodes.ConnectionTimeout => NotificationType.NETWORK_REACHABILITY_ISSUE,
                SessionErrorCodes.ConnectionNetworkError => NotificationType.NETWORK_REACHABILITY_ISSUE,
                SessionErrorCodes.ConnectionServiceUnavailable => NotificationType.NETWORK_REACHABILITY_ISSUE,
                SessionErrorCodes.ConnectionServerError => NotificationType.NETWORK_REACHABILITY_ISSUE,
                SessionErrorCodes.TransportLivekitError => NotificationType.NETWORK_REACHABILITY_ISSUE,
                SessionErrorCodes.ConnectionFailed => NotificationType.NETWORK_REACHABILITY_ISSUE,

                // Microphone errors
                SessionErrorCodes.AudioMicUnavailable => NotificationType.NO_MICROPHONE_DETECTED,
                SessionErrorCodes.AudioMicPermissionDenied => NotificationType.MICROPHONE_ISSUE,
                SessionErrorCodes.AudioMicPublishFailed => NotificationType.MICROPHONE_ISSUE,

                _ => MapByPrefix(errorCode)
            };
        }

        private static NotificationType? MapByPrefix(string errorCode)
        {
            if (errorCode.StartsWith("connection.", StringComparison.OrdinalIgnoreCase))
                return NotificationType.NETWORK_REACHABILITY_ISSUE;

            if (errorCode.StartsWith("transport.", StringComparison.OrdinalIgnoreCase))
                return NotificationType.NETWORK_REACHABILITY_ISSUE;

            if (errorCode.StartsWith("audio.", StringComparison.OrdinalIgnoreCase))
                return NotificationType.MICROPHONE_ISSUE;

            if (errorCode.StartsWith("config.", StringComparison.OrdinalIgnoreCase))
                return NotificationType.API_KEY_NOT_FOUND;

            return null;
        }

        private bool TryPassCooldown(NotificationType type)
        {
            float now = Time.realtimeSinceStartup;

            lock (_cooldownLock)
            {
                if (_lastShownTime.TryGetValue(type, out float lastTime))
                {
                    if (now - lastTime < CooldownSeconds)
                        return false;
                }

                _lastShownTime[type] = now;
                return true;
            }
        }

        /// <summary>
        ///     Manually requests a notification (for pre-flight checks in ConvaiRoomManager).
        ///     Respects settings and cooldown.
        /// </summary>
        public void RequestNotificationIfEnabled(NotificationType type)
        {
            if (!_isNotificationEnabled()) return;

            IConvaiNotificationService notificationService = _notificationServiceAccessor?.Invoke();
            if (notificationService == null) return;

            if (!TryPassCooldown(type)) return;

            notificationService.RequestNotification(type);
        }

        /// <summary>
        ///     Clears cooldown state (useful for testing or scene changes).
        /// </summary>
        public void ClearCooldowns()
        {
            lock (_cooldownLock) _lastShownTime.Clear();
        }
    }
}
