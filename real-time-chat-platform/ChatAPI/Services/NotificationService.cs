using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ChatAPI.Data;
using ChatAPI.Models;
using ChatAPI.Hubs;

namespace ChatAPI.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ChatDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            ChatDbContext context,
            IHubContext<ChatHub> hubContext,
            ILogger<NotificationService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task<NotificationDto> CreateNotificationAsync(
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
            DateTime? expiresAt = null)
        {
            try
            {
                // Check if user should receive this type of notification
                var shouldSend = await ShouldSendNotificationAsync(userId, type);
                if (!shouldSend)
                {
                    _logger.LogDebug("Notification of type {Type} not sent to user {UserId} due to settings", type, userId);
                    return null!;
                }

                var notification = new Notification
                {
                    UserId = userId,
                    Title = title,
                    Message = message,
                    Type = type,
                    Priority = priority,
                    IsActionable = isActionable,
                    ActionType = actionType,
                    ActionData = actionData,
                    RelatedMessageId = relatedMessageId,
                    RelatedChatRoomId = relatedChatRoomId,
                    RelatedUserId = relatedUserId,
                    ExpiresAt = expiresAt,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                var notificationDto = await MapToNotificationDto(notification);

                // Send real-time notification
                await SendRealTimeNotificationAsync(userId, notificationDto);

                _logger.LogInformation("Notification created for user {UserId}: {Title}", userId, title);

                return notificationDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notification for user {UserId}", userId);
                throw;
            }
        }

        public async Task<List<NotificationDto>> CreateBulkNotificationsAsync(
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
            DateTime? expiresAt = null)
        {
            try
            {
                var notifications = new List<Notification>();
                var notificationDtos = new List<NotificationDto>();

                foreach (var userId in userIds)
                {
                    var shouldSend = await ShouldSendNotificationAsync(userId, type);
                    if (shouldSend)
                    {
                        var notification = new Notification
                        {
                            UserId = userId,
                            Title = title,
                            Message = message,
                            Type = type,
                            Priority = priority,
                            IsActionable = isActionable,
                            ActionType = actionType,
                            ActionData = actionData,
                            RelatedMessageId = relatedMessageId,
                            RelatedChatRoomId = relatedChatRoomId,
                            RelatedUserId = relatedUserId,
                            ExpiresAt = expiresAt,
                            CreatedAt = DateTime.UtcNow
                        };

                        notifications.Add(notification);
                    }
                }

                if (notifications.Any())
                {
                    _context.Notifications.AddRange(notifications);
                    await _context.SaveChangesAsync();

                    foreach (var notification in notifications)
                    {
                        var dto = await MapToNotificationDto(notification);
                        notificationDtos.Add(dto);
                    }

                    // Send real-time notifications
                    await SendRealTimeNotificationToMultipleUsersAsync(
                        notifications.Select(n => n.UserId).ToList(),
                        notificationDtos.First()
                    );

                    _logger.LogInformation("Bulk notifications created for {Count} users: {Title}",
                        notifications.Count, title);
                }

                return notificationDtos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating bulk notifications");
                throw;
            }
        }

        public async Task<NotificationPagedResult> GetUserNotificationsAsync(
            Guid userId,
            NotificationSearchDto searchDto)
        {
            try
            {
                var query = _context.Notifications
                    .Where(n => n.UserId == userId)
                    .Include(n => n.RelatedMessage)
                        .ThenInclude(m => m!.User)
                    .Include(n => n.RelatedChatRoom)
                    .Include(n => n.RelatedUser)
                    .AsQueryable();

                // Apply filters
                if (searchDto.Type.HasValue)
                    query = query.Where(n => n.Type == searchDto.Type.Value);

                if (searchDto.Priority.HasValue)
                    query = query.Where(n => n.Priority == searchDto.Priority.Value);

                if (searchDto.IsRead.HasValue)
                    query = query.Where(n => n.IsRead == searchDto.IsRead.Value);

                if (searchDto.IsActionable.HasValue)
                    query = query.Where(n => n.IsActionable == searchDto.IsActionable.Value);

                if (searchDto.FromDate.HasValue)
                    query = query.Where(n => n.CreatedAt >= searchDto.FromDate.Value);

                if (searchDto.ToDate.HasValue)
                    query = query.Where(n => n.CreatedAt <= searchDto.ToDate.Value);

                // Order by creation date (newest first)
                query = query.OrderByDescending(n => n.CreatedAt);

                var totalCount = await query.CountAsync();
                var unreadCount = await _context.Notifications
                    .Where(n => n.UserId == userId && !n.IsRead)
                    .CountAsync();

                var notifications = await query
                    .Skip((searchDto.Page - 1) * searchDto.PageSize)
                    .Take(searchDto.PageSize)
                    .ToListAsync();

                var notificationDtos = new List<NotificationDto>();
                foreach (var notification in notifications)
                {
                    notificationDtos.Add(await MapToNotificationDto(notification));
                }

                return new NotificationPagedResult
                {
                    Notifications = notificationDtos,
                    TotalCount = totalCount,
                    UnreadCount = unreadCount,
                    Page = searchDto.Page,
                    PageSize = searchDto.PageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / searchDto.PageSize),
                    HasNextPage = searchDto.Page * searchDto.PageSize < totalCount,
                    HasPreviousPage = searchDto.Page > 1
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notifications for user {UserId}", userId);
                throw;
            }
        }

        public async Task<NotificationStatsDto> GetUserNotificationStatsAsync(Guid userId)
        {
            try
            {
                var notifications = await _context.Notifications
                    .Where(n => n.UserId == userId)
                    .ToListAsync();

                var stats = new NotificationStatsDto
                {
                    TotalNotifications = notifications.Count,
                    UnreadNotifications = notifications.Count(n => !n.IsRead),
                    LastNotificationTime = notifications.Any() ? notifications.Max(n => n.CreatedAt) : null
                };

                // Group by type
                foreach (var type in Enum.GetValues<NotificationType>())
                {
                    stats.NotificationsByType[type] = notifications.Count(n => n.Type == type);
                }

                // Group by priority
                foreach (var priority in Enum.GetValues<NotificationPriority>())
                {
                    stats.NotificationsByPriority[priority] = notifications.Count(n => n.Priority == priority);
                }

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notification stats for user {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> MarkNotificationAsReadAsync(Guid notificationId, Guid userId)
        {
            try
            {
                var notification = await _context.Notifications
                    .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

                if (notification == null)
                    return false;

                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogDebug("Notification {NotificationId} marked as read for user {UserId}",
                    notificationId, userId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification {NotificationId} as read for user {UserId}",
                    notificationId, userId);
                throw;
            }
        }

        public async Task<int> MarkNotificationsAsReadAsync(MarkNotificationsReadDto markReadDto, Guid userId)
        {
            try
            {
                var query = _context.Notifications
                    .Where(n => n.UserId == userId && !n.IsRead);

                if (markReadDto.NotificationIds?.Any() == true)
                {
                    query = query.Where(n => markReadDto.NotificationIds.Contains(n.Id));
                }

                if (markReadDto.Type.HasValue)
                {
                    query = query.Where(n => n.Type == markReadDto.Type.Value);
                }

                if (markReadDto.BeforeDate.HasValue)
                {
                    query = query.Where(n => n.CreatedAt <= markReadDto.BeforeDate.Value);
                }

                var notifications = await query.ToListAsync();

                foreach (var notification in notifications)
                {
                    notification.IsRead = true;
                    notification.ReadAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Marked {Count} notifications as read for user {UserId}",
                    notifications.Count, userId);

                return notifications.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notifications as read for user {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> DeleteNotificationAsync(Guid notificationId, Guid userId)
        {
            try
            {
                var notification = await _context.Notifications
                    .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

                if (notification == null)
                    return false;

                _context.Notifications.Remove(notification);
                await _context.SaveChangesAsync();

                _logger.LogDebug("Notification {NotificationId} deleted for user {UserId}",
                    notificationId, userId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting notification {NotificationId} for user {UserId}",
                    notificationId, userId);
                throw;
            }
        }

        public async Task<int> DeleteExpiredNotificationsAsync()
        {
            try
            {
                var expiredNotifications = await _context.Notifications
                    .Where(n => n.ExpiresAt.HasValue && n.ExpiresAt.Value <= DateTime.UtcNow)
                    .ToListAsync();

                if (expiredNotifications.Any())
                {
                    _context.Notifications.RemoveRange(expiredNotifications);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Deleted {Count} expired notifications", expiredNotifications.Count);
                }

                return expiredNotifications.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting expired notifications");
                throw;
            }
        }

        public async Task<NotificationSettingsDto> GetUserNotificationSettingsAsync(Guid userId)
        {
            try
            {
                var settings = await _context.NotificationSettings
                    .FirstOrDefaultAsync(ns => ns.UserId == userId);

                if (settings == null)
                {
                    // Create default settings
                    settings = new NotificationSettings
                    {
                        UserId = userId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _context.NotificationSettings.Add(settings);
                    await _context.SaveChangesAsync();
                }

                return MapToNotificationSettingsDto(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notification settings for user {UserId}", userId);
                throw;
            }
        }

        public async Task<NotificationSettingsDto> UpdateUserNotificationSettingsAsync(
            Guid userId,
            UpdateNotificationSettingsDto updateDto)
        {
            try
            {
                var settings = await _context.NotificationSettings
                    .FirstOrDefaultAsync(ns => ns.UserId == userId);

                if (settings == null)
                {
                    settings = new NotificationSettings
                    {
                        UserId = userId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.NotificationSettings.Add(settings);
                }

                // Update only provided fields
                if (updateDto.BrowserNotifications.HasValue)
                    settings.BrowserNotifications = updateDto.BrowserNotifications.Value;

                if (updateDto.EmailNotifications.HasValue)
                    settings.EmailNotifications = updateDto.EmailNotifications.Value;

                if (updateDto.PushNotifications.HasValue)
                    settings.PushNotifications = updateDto.PushNotifications.Value;

                if (updateDto.SoundNotifications.HasValue)
                    settings.SoundNotifications = updateDto.SoundNotifications.Value;

                if (updateDto.NotifyOnNewMessage.HasValue)
                    settings.NotifyOnNewMessage = updateDto.NotifyOnNewMessage.Value;

                if (updateDto.NotifyOnMention.HasValue)
                    settings.NotifyOnMention = updateDto.NotifyOnMention.Value;

                if (updateDto.NotifyOnUserOnline.HasValue)
                    settings.NotifyOnUserOnline = updateDto.NotifyOnUserOnline.Value;

                if (updateDto.NotifyOnUserOffline.HasValue)
                    settings.NotifyOnUserOffline = updateDto.NotifyOnUserOffline.Value;

                if (updateDto.NotifyOnPrivateMessage.HasValue)
                    settings.NotifyOnPrivateMessage = updateDto.NotifyOnPrivateMessage.Value;

                if (updateDto.NotifyOnSystemMessage.HasValue)
                    settings.NotifyOnSystemMessage = updateDto.NotifyOnSystemMessage.Value;

                if (updateDto.NotifyOnChatRoomInvite.HasValue)
                    settings.NotifyOnChatRoomInvite = updateDto.NotifyOnChatRoomInvite.Value;

                if (updateDto.DoNotDisturbEnabled.HasValue)
                    settings.DoNotDisturbEnabled = updateDto.DoNotDisturbEnabled.Value;

                if (updateDto.DoNotDisturbStartTime.HasValue)
                    settings.DoNotDisturbStartTime = updateDto.DoNotDisturbStartTime.Value;

                if (updateDto.DoNotDisturbEndTime.HasValue)
                    settings.DoNotDisturbEndTime = updateDto.DoNotDisturbEndTime.Value;

                settings.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Notification settings updated for user {UserId}", userId);

                return MapToNotificationSettingsDto(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating notification settings for user {UserId}", userId);
                throw;
            }
        }

        public async Task<NotificationSettingsDto> ResetUserNotificationSettingsAsync(Guid userId)
        {
            try
            {
                var settings = await _context.NotificationSettings
                    .FirstOrDefaultAsync(ns => ns.UserId == userId);

                if (settings != null)
                {
                    _context.NotificationSettings.Remove(settings);
                }

                // Create new default settings
                var newSettings = new NotificationSettings
                {
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.NotificationSettings.Add(newSettings);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Notification settings reset to defaults for user {UserId}", userId);

                return MapToNotificationSettingsDto(newSettings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting notification settings for user {UserId}", userId);
                throw;
            }
        }

        public async Task SendRealTimeNotificationAsync(Guid userId, NotificationDto notification)
        {
            try
            {
                var userConnections = await _context.UserConnections
                    .Where(c => c.UserId == userId)
                    .Select(c => c.ConnectionId)
                    .ToListAsync();

                if (userConnections.Any())
                {
                    await _hubContext.Clients.Clients(userConnections)
                        .SendAsync("Notification", notification);

                    _logger.LogDebug("Real-time notification sent to user {UserId} on {ConnectionCount} connections",
                        userId, userConnections.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending real-time notification to user {UserId}", userId);
            }
        }

        public async Task SendRealTimeNotificationToMultipleUsersAsync(List<Guid> userIds, NotificationDto notification)
        {
            try
            {
                var userConnections = await _context.UserConnections
                    .Where(c => userIds.Contains(c.UserId))
                    .Select(c => c.ConnectionId)
                    .ToListAsync();

                if (userConnections.Any())
                {
                    await _hubContext.Clients.Clients(userConnections)
                        .SendAsync("Notification", notification);

                    _logger.LogDebug("Real-time notification sent to {UserCount} users on {ConnectionCount} connections",
                        userIds.Count, userConnections.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending real-time notification to multiple users");
            }
        }

        // Specific notification type methods...
        public async Task NotifyNewMessageAsync(
            Guid recipientUserId,
            string senderUsername,
            string messageContent,
            Guid messageId,
            Guid chatRoomId,
            Guid senderId)
        {
            await CreateNotificationAsync(
                recipientUserId,
                $"New message from {senderUsername}",
                messageContent.Length > 100 ? messageContent.Substring(0, 100) + "..." : messageContent,
                NotificationType.Message,
                NotificationPriority.Normal,
                true,
                "view_message",
                null,
                messageId,
                chatRoomId,
                senderId
            );
        }

        public async Task NotifyUserMentionAsync(
            Guid mentionedUserId,
            string senderUsername,
            string messageContent,
            Guid messageId,
            Guid chatRoomId,
            Guid senderId)
        {
            await CreateNotificationAsync(
                mentionedUserId,
                $"{senderUsername} mentioned you",
                messageContent.Length > 100 ? messageContent.Substring(0, 100) + "..." : messageContent,
                NotificationType.Mention,
                NotificationPriority.High,
                true,
                "view_mention",
                null,
                messageId,
                chatRoomId,
                senderId
            );
        }

        public async Task NotifyUserOnlineAsync(Guid userId, string username, List<Guid> recipientUserIds)
        {
            await CreateBulkNotificationsAsync(
                recipientUserIds,
                $"{username} is now online",
                $"{username} joined the chat",
                NotificationType.UserOnline,
                NotificationPriority.Low,
                false,
                null,
                null,
                null,
                null,
                userId
            );
        }

        public async Task NotifyUserOfflineAsync(Guid userId, string username, List<Guid> recipientUserIds)
        {
            await CreateBulkNotificationsAsync(
                recipientUserIds,
                $"{username} went offline",
                $"{username} left the chat",
                NotificationType.UserOffline,
                NotificationPriority.Low,
                false,
                null,
                null,
                null,
                null,
                userId
            );
        }

        public async Task NotifyPrivateMessageAsync(
            Guid recipientUserId,
            string senderUsername,
            string messageContent,
            Guid messageId,
            Guid chatRoomId,
            Guid senderId)
        {
            await CreateNotificationAsync(
                recipientUserId,
                $"Private message from {senderUsername}",
                messageContent.Length > 100 ? messageContent.Substring(0, 100) + "..." : messageContent,
                NotificationType.PrivateMessage,
                NotificationPriority.High,
                true,
                "view_private_message",
                null,
                messageId,
                chatRoomId,
                senderId
            );
        }

        public async Task NotifySystemMessageAsync(
            List<Guid> userIds,
            string title,
            string message,
            NotificationPriority priority = NotificationPriority.Normal)
        {
            await CreateBulkNotificationsAsync(
                userIds,
                title,
                message,
                NotificationType.System,
                priority
            );
        }

        public async Task NotifyChatRoomInviteAsync(
            Guid recipientUserId,
            string inviterUsername,
            string chatRoomName,
            Guid chatRoomId,
            Guid inviterId)
        {
            await CreateNotificationAsync(
                recipientUserId,
                $"Chat room invitation from {inviterUsername}",
                $"You've been invited to join {chatRoomName}",
                NotificationType.ChatRoomInvite,
                NotificationPriority.Normal,
                true,
                "view_chat_room_invite",
                null,
                null,
                chatRoomId,
                inviterId
            );
        }

        public async Task<bool> ShouldSendNotificationAsync(Guid userId, NotificationType type)
        {
            try
            {
                var settings = await _context.NotificationSettings
                    .FirstOrDefaultAsync(ns => ns.UserId == userId);

                if (settings == null)
                    return true; // Default to sending notifications

                // Check do not disturb mode
                if (settings.DoNotDisturbEnabled && IsInDoNotDisturbPeriod(settings))
                    return false;

                // Check type-specific settings
                return type switch
                {
                    NotificationType.Message => settings.NotifyOnNewMessage,
                    NotificationType.Mention => settings.NotifyOnMention,
                    NotificationType.UserOnline => settings.NotifyOnUserOnline,
                    NotificationType.UserOffline => settings.NotifyOnUserOffline,
                    NotificationType.PrivateMessage => settings.NotifyOnPrivateMessage,
                    NotificationType.System => settings.NotifyOnSystemMessage,
                    NotificationType.ChatRoomInvite => settings.NotifyOnChatRoomInvite,
                    _ => true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if notification should be sent to user {UserId}", userId);
                return true; // Default to sending on error
            }
        }

        public async Task CleanupOldNotificationsAsync(int daysToKeep = 30)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
                var oldNotifications = await _context.Notifications
                    .Where(n => n.CreatedAt < cutoffDate)
                    .ToListAsync();

                if (oldNotifications.Any())
                {
                    _context.Notifications.RemoveRange(oldNotifications);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Cleaned up {Count} old notifications older than {Days} days",
                        oldNotifications.Count, daysToKeep);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up old notifications");
                throw;
            }
        }

        private async Task<NotificationDto> MapToNotificationDto(Notification notification)
        {
            var dto = new NotificationDto
            {
                Id = notification.Id,
                UserId = notification.UserId,
                Title = notification.Title,
                Message = notification.Message,
                Type = notification.Type,
                Priority = notification.Priority,
                IsRead = notification.IsRead,
                IsActionable = notification.IsActionable,
                ActionType = notification.ActionType,
                ActionData = notification.ActionData,
                RelatedMessageId = notification.RelatedMessageId,
                RelatedChatRoomId = notification.RelatedChatRoomId,
                RelatedUserId = notification.RelatedUserId,
                CreatedAt = notification.CreatedAt,
                ReadAt = notification.ReadAt,
                ExpiresAt = notification.ExpiresAt
            };

            // Map related entities if they exist
            if (notification.RelatedMessage != null)
            {
                dto.RelatedMessage = new MessageDto
                {
                    Id = notification.RelatedMessage.Id,
                    Content = notification.RelatedMessage.Content,
                    Type = notification.RelatedMessage.Type,
                    CreatedAt = notification.RelatedMessage.CreatedAt,
                    User = new UserDto
                    {
                        Id = notification.RelatedMessage.User.Id,
                        Username = notification.RelatedMessage.User.Username,
                        Avatar = notification.RelatedMessage.User.Avatar
                    }
                };
            }

            if (notification.RelatedChatRoom != null)
            {
                dto.RelatedChatRoom = new ChatRoomDto
                {
                    Id = notification.RelatedChatRoom.Id,
                    Name = notification.RelatedChatRoom.Name,
                    Type = notification.RelatedChatRoom.Type,
                    Avatar = notification.RelatedChatRoom.Avatar
                };
            }

            if (notification.RelatedUser != null)
            {
                dto.RelatedUser = new UserDto
                {
                    Id = notification.RelatedUser.Id,
                    Username = notification.RelatedUser.Username,
                    Avatar = notification.RelatedUser.Avatar,
                    IsOnline = notification.RelatedUser.IsOnline
                };
            }

            return dto;
        }

        private static NotificationSettingsDto MapToNotificationSettingsDto(NotificationSettings settings)
        {
            return new NotificationSettingsDto
            {
                Id = settings.Id,
                UserId = settings.UserId,
                BrowserNotifications = settings.BrowserNotifications,
                EmailNotifications = settings.EmailNotifications,
                PushNotifications = settings.PushNotifications,
                SoundNotifications = settings.SoundNotifications,
                NotifyOnNewMessage = settings.NotifyOnNewMessage,
                NotifyOnMention = settings.NotifyOnMention,
                NotifyOnUserOnline = settings.NotifyOnUserOnline,
                NotifyOnUserOffline = settings.NotifyOnUserOffline,
                NotifyOnPrivateMessage = settings.NotifyOnPrivateMessage,
                NotifyOnSystemMessage = settings.NotifyOnSystemMessage,
                NotifyOnChatRoomInvite = settings.NotifyOnChatRoomInvite,
                DoNotDisturbEnabled = settings.DoNotDisturbEnabled,
                DoNotDisturbStartTime = settings.DoNotDisturbStartTime,
                DoNotDisturbEndTime = settings.DoNotDisturbEndTime,
                UpdatedAt = settings.UpdatedAt
            };
        }

        private static bool IsInDoNotDisturbPeriod(NotificationSettings settings)
        {
            if (!settings.DoNotDisturbEnabled ||
                !settings.DoNotDisturbStartTime.HasValue ||
                !settings.DoNotDisturbEndTime.HasValue)
                return false;

            var now = DateTime.Now.TimeOfDay;
            var start = settings.DoNotDisturbStartTime.Value;
            var end = settings.DoNotDisturbEndTime.Value;

            if (start <= end)
            {
                return now >= start && now <= end;
            }
            else
            {
                // Crosses midnight
                return now >= start || now <= end;
            }
        }
    }
} 