using System;
using Convai.Runtime.Presentation.Views.Notifications;
using Convai.Shared.Interfaces;
using Convai.Shared.Types;
using UnityEngine.Scripting;

namespace Convai.Sample.UI.Notifications
{
    /// <summary>
    ///     Sample implementation of IConvaiNotificationService for displaying notifications to the user.
    /// </summary>
    /// <remarks>
    ///     This is a reference implementation in the Sample layer.
    ///     This class can be instantiated manually or registered as a service.
    ///     The [Preserve] attribute prevents IL2CPP from stripping it.
    /// </remarks>
    [Preserve]
    public class NotificationService : IConvaiNotificationService
    {
        public event Action<NotificationType> OnNotificationRequested = delegate { };
        public event Action OnNotificationDismissed = delegate { };

        public void RequestNotification(NotificationType notificationType) =>
            OnNotificationRequested?.Invoke(notificationType);

        public void DismissNotification() => OnNotificationDismissed?.Invoke();
        public event Action<SONotification> OnCustomNotificationRequested = delegate { };

        public void RequestCustomNotification(SONotification notification) =>
            OnCustomNotificationRequested?.Invoke(notification);
    }
}
