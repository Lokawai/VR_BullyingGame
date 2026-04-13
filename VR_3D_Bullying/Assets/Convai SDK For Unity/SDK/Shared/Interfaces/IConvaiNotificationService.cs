using System;
using Convai.Shared.Types;

namespace Convai.Shared.Interfaces
{
    /// <summary>
    ///     Shared interface for the notification service.
    /// </summary>
    /// <remarks>
    ///     This interface is defined in Convai.Shared to enable cross-assembly type safety
    ///     for notification handling without creating cyclic dependencies.
    /// </remarks>
    public interface IConvaiNotificationService
    {
        /// <summary>
        ///     Event raised when a notification should be displayed.
        /// </summary>
        public event Action<NotificationType> OnNotificationRequested;

        /// <summary>
        ///     Event raised when a notification should be hidden.
        /// </summary>
        public event Action OnNotificationDismissed;

        /// <summary>
        ///     Requests a notification to be displayed.
        /// </summary>
        /// <param name="notificationType">The type of notification to display.</param>
        public void RequestNotification(NotificationType notificationType);

        /// <summary>
        ///     Dismisses the current notification.
        /// </summary>
        public void DismissNotification();
    }
}
