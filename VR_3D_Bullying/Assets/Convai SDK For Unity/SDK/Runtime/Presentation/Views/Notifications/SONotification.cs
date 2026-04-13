using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Presentation.Views.Notifications
{
    /// <summary>
    ///     This class represents a notification in the game.
    /// </summary>
    [CreateAssetMenu(menuName = "Convai/Notification System/Notification", fileName = "New Notification")]
    public class SONotification : ScriptableObject
    {
        /// <summary>
        ///     The type of the notification.
        /// </summary>
        [Tooltip("The type of the notification.")]
        public NotificationType notificationType;

        /// <summary>
        ///     The icon to be displayed with the notification.
        /// </summary>
        [Tooltip("The icon to be displayed with the notification.")]
        public Sprite icon;

        /// <summary>
        ///     The notification title.
        /// </summary>
        [Tooltip("The notification title.")] public string notificationTitle;

        /// <summary>
        ///     The text content of the notification.
        /// </summary>
        [TextArea(10, 10)] [Tooltip("The text content of the notification.")]
        public string notificationMessage;

        public SONotification SetNotificationType(NotificationType type)
        {
            notificationType = type;
            return this;
        }

        public SONotification SetIcon(Sprite newIcon)
        {
            icon = newIcon;
            return this;
        }

        public SONotification SetTitle(string title)
        {
            notificationTitle = title;
            return this;
        }

        public SONotification SetMessage(string message)
        {
            notificationMessage = message;
            return this;
        }

        public override string ToString() =>
            $"Type: {notificationType} | Title: {notificationTitle} | Message: {notificationMessage}";
    }
}
