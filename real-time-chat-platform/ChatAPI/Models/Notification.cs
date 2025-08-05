using System.ComponentModel.DataAnnotations;

namespace ChatAPI.Models
{
    public class Notification
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string Message { get; set; } = string.Empty;

        public NotificationType Type { get; set; } = NotificationType.Info;

        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

        public bool IsRead { get; set; } = false;

        public bool IsActionable { get; set; } = false;

        [StringLength(100)]
        public string? ActionType { get; set; }

        [StringLength(1000)]
        public string? ActionData { get; set; }

        // Related entity references
        public Guid? RelatedMessageId { get; set; }
        public Guid? RelatedChatRoomId { get; set; }
        public Guid? RelatedUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReadAt { get; set; }

        public DateTime? ExpiresAt { get; set; }

        // Navigation properties
        public virtual User User { get; set; } = null!;
        public virtual Message? RelatedMessage { get; set; }
        public virtual ChatRoom? RelatedChatRoom { get; set; }
        public virtual User? RelatedUser { get; set; }
    }

    public enum NotificationType
    {
        Info = 0,
        Success = 1,
        Warning = 2,
        Error = 3,
        Message = 4,
        Mention = 5,
        UserOnline = 6,
        UserOffline = 7,
        PrivateMessage = 8,
        System = 9,
        ChatRoomInvite = 10,
        FileUpload = 11
    }

    public enum NotificationPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Urgent = 3
    }

    public class NotificationDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; }
        public NotificationPriority Priority { get; set; }
        public bool IsRead { get; set; }
        public bool IsActionable { get; set; }
        public string? ActionType { get; set; }
        public string? ActionData { get; set; }
        public Guid? RelatedMessageId { get; set; }
        public Guid? RelatedChatRoomId { get; set; }
        public Guid? RelatedUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime? ExpiresAt { get; set; }

        // Related entities
        public MessageDto? RelatedMessage { get; set; }
        public ChatRoomDto? RelatedChatRoom { get; set; }
        public UserDto? RelatedUser { get; set; }
    }

    public class CreateNotificationDto
    {
        [Required]
        public Guid UserId { get; set; }

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string Message { get; set; } = string.Empty;

        public NotificationType Type { get; set; } = NotificationType.Info;

        public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

        public bool IsActionable { get; set; } = false;

        public string? ActionType { get; set; }
        public string? ActionData { get; set; }

        public Guid? RelatedMessageId { get; set; }
        public Guid? RelatedChatRoomId { get; set; }
        public Guid? RelatedUserId { get; set; }

        public DateTime? ExpiresAt { get; set; }
    }

    public class NotificationSearchDto
    {
        public NotificationType? Type { get; set; }
        public NotificationPriority? Priority { get; set; }
        public bool? IsRead { get; set; }
        public bool? IsActionable { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class NotificationPagedResult
    {
        public List<NotificationDto> Notifications { get; set; } = new();
        public int TotalCount { get; set; }
        public int UnreadCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasNextPage { get; set; }
        public bool HasPreviousPage { get; set; }
    }

    public class NotificationStatsDto
    {
        public int TotalNotifications { get; set; }
        public int UnreadNotifications { get; set; }
        public Dictionary<NotificationType, int> NotificationsByType { get; set; } = new();
        public Dictionary<NotificationPriority, int> NotificationsByPriority { get; set; } = new();
        public DateTime? LastNotificationTime { get; set; }
    }

    public class MarkNotificationsReadDto
    {
        public List<Guid>? NotificationIds { get; set; }
        public bool MarkAllAsRead { get; set; } = false;
        public NotificationType? Type { get; set; }
        public DateTime? BeforeDate { get; set; }
    }

    public class NotificationSettings
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }

        public bool BrowserNotifications { get; set; } = true;
        public bool EmailNotifications { get; set; } = false;
        public bool PushNotifications { get; set; } = false;
        public bool SoundNotifications { get; set; } = true;

        // Notification type preferences
        public bool NotifyOnNewMessage { get; set; } = true;
        public bool NotifyOnMention { get; set; } = true;
        public bool NotifyOnUserOnline { get; set; } = false;
        public bool NotifyOnUserOffline { get; set; } = false;
        public bool NotifyOnPrivateMessage { get; set; } = true;
        public bool NotifyOnSystemMessage { get; set; } = true;
        public bool NotifyOnChatRoomInvite { get; set; } = true;

        // Do not disturb settings
        public bool DoNotDisturbEnabled { get; set; } = false;
        public TimeSpan? DoNotDisturbStartTime { get; set; }
        public TimeSpan? DoNotDisturbEndTime { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public virtual User User { get; set; } = null!;
    }

    public class NotificationSettingsDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public bool BrowserNotifications { get; set; }
        public bool EmailNotifications { get; set; }
        public bool PushNotifications { get; set; }
        public bool SoundNotifications { get; set; }
        public bool NotifyOnNewMessage { get; set; }
        public bool NotifyOnMention { get; set; }
        public bool NotifyOnUserOnline { get; set; }
        public bool NotifyOnUserOffline { get; set; }
        public bool NotifyOnPrivateMessage { get; set; }
        public bool NotifyOnSystemMessage { get; set; }
        public bool NotifyOnChatRoomInvite { get; set; }
        public bool DoNotDisturbEnabled { get; set; }
        public TimeSpan? DoNotDisturbStartTime { get; set; }
        public TimeSpan? DoNotDisturbEndTime { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class UpdateNotificationSettingsDto
    {
        public bool? BrowserNotifications { get; set; }
        public bool? EmailNotifications { get; set; }
        public bool? PushNotifications { get; set; }
        public bool? SoundNotifications { get; set; }
        public bool? NotifyOnNewMessage { get; set; }
        public bool? NotifyOnMention { get; set; }
        public bool? NotifyOnUserOnline { get; set; }
        public bool? NotifyOnUserOffline { get; set; }
        public bool? NotifyOnPrivateMessage { get; set; }
        public bool? NotifyOnSystemMessage { get; set; }
        public bool? NotifyOnChatRoomInvite { get; set; }
        public bool? DoNotDisturbEnabled { get; set; }
        public TimeSpan? DoNotDisturbStartTime { get; set; }
        public TimeSpan? DoNotDisturbEndTime { get; set; }
    }
} 