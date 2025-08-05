using ChatAPI.Models;

namespace ChatAPI.Services
{
    public interface INotificationService
    {
        // Notification Management
        Task<NotificationDto> CreateNotificationAsync(
            Guid userId,
            string title,
            string message,
            NotificationType type = NotificationType.Info,
            NotificationPriority priority = NotificationPriority.Normal,
            bool isActionable = false,
            string? actionType = null,
            string? actionData = null,
            Guid? relatedMessageId = null,
            Guid? relatedChatRoomId = null,
            Guid? relatedUserId = null,
            DateTime? expiresAt = null
        );

        Task<List<NotificationDto>> CreateBulkNotificationsAsync(
            List<Guid> userIds,
            string title,
            string message,
            NotificationType type = NotificationType.Info,
            NotificationPriority priority = NotificationPriority.Normal,
            bool isActionable = false,
            string? actionType = null,
            string? actionData = null,
            Guid? relatedMessageId = null,
            Guid? relatedChatRoomId = null,
            Guid? relatedUserId = null,
            DateTime? expiresAt = null
        );

        Task<NotificationPagedResult> GetUserNotificationsAsync(
            Guid userId,
            NotificationSearchDto searchDto
        );

        Task<NotificationStatsDto> GetUserNotificationStatsAsync(Guid userId);

        Task<bool> MarkNotificationAsReadAsync(Guid notificationId, Guid userId);

        Task<int> MarkNotificationsAsReadAsync(MarkNotificationsReadDto markReadDto, Guid userId);

        Task<bool> DeleteNotificationAsync(Guid notificationId, Guid userId);

        Task<int> DeleteExpiredNotificationsAsync();

        // Notification Settings
        Task<NotificationSettingsDto> GetUserNotificationSettingsAsync(Guid userId);

        Task<NotificationSettingsDto> UpdateUserNotificationSettingsAsync(
            Guid userId,
            UpdateNotificationSettingsDto updateDto
        );

        Task<NotificationSettingsDto> ResetUserNotificationSettingsAsync(Guid userId);

        // Real-time Notifications
        Task SendRealTimeNotificationAsync(Guid userId, NotificationDto notification);

        Task SendRealTimeNotificationToMultipleUsersAsync(List<Guid> userIds, NotificationDto notification);

        // Specific Notification Types
        Task NotifyNewMessageAsync(
            Guid recipientUserId,
            string senderUsername,
            string messageContent,
            Guid messageId,
            Guid chatRoomId,
            Guid senderId
        );

        Task NotifyUserMentionAsync(
            Guid mentionedUserId,
            string senderUsername,
            string messageContent,
            Guid messageId,
            Guid chatRoomId,
            Guid senderId
        );

        Task NotifyUserOnlineAsync(Guid userId, string username, List<Guid> recipientUserIds);

        Task NotifyUserOfflineAsync(Guid userId, string username, List<Guid> recipientUserIds);

        Task NotifyPrivateMessageAsync(
            Guid recipientUserId,
            string senderUsername,
            string messageContent,
            Guid messageId,
            Guid chatRoomId,
            Guid senderId
        );

        Task NotifySystemMessageAsync(
            List<Guid> userIds,
            string title,
            string message,
            NotificationPriority priority = NotificationPriority.Normal
        );

        Task NotifyChatRoomInviteAsync(
            Guid recipientUserId,
            string inviterUsername,
            string chatRoomName,
            Guid chatRoomId,
            Guid inviterId
        );

        // Utility Methods
        Task<bool> ShouldSendNotificationAsync(Guid userId, NotificationType type);

        Task CleanupOldNotificationsAsync(int daysToKeep = 30);
    }
} 