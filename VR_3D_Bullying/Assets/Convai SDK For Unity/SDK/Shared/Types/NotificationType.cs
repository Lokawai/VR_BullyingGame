namespace Convai.Shared.Types
{
    /// <summary>
    ///     Enumeration defining various types of in-app notifications.
    ///     Each enum value represents a specific scenario or issue that can trigger a notification.
    /// </summary>
    /// <remarks>
    ///     This enum is shared across assemblies to enable compile-time type-safe notification handling.
    ///     Used by notification handlers and the notification service.
    /// </remarks>
    public enum NotificationType
    {
        /// <summary>
        ///     Indicates a notification related to microphone problems.
        /// </summary>
        MICROPHONE_ISSUE,

        /// <summary>
        ///     Indicates a notification related to network reachability issues.
        /// </summary>
        NETWORK_REACHABILITY_ISSUE,

        /// <summary>
        ///     Indicates a notification when the player is not in proximity to initiate a conversation.
        /// </summary>
        NOT_CLOSE_ENOUGH_FOR_CONVERSATION,

        /// <summary>
        ///     Indicates that no microphone device was detected in the system.
        /// </summary>
        NO_MICROPHONE_DETECTED,

        /// <summary>
        ///     Indicates that no API key was found.
        /// </summary>
        API_KEY_NOT_FOUND,

        /// <summary>
        ///     Indicates that usage limit for current plan has exceeded.
        /// </summary>
        USAGE_LIMIT_EXCEEDED
    }
}
