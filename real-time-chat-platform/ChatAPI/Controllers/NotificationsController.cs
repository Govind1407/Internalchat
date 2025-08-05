using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ChatAPI.Models;
using ChatAPI.Services;

namespace ChatAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            INotificationService notificationService,
            ILogger<NotificationsController> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        /// <summary>
        /// Get user notifications with pagination and filtering
        /// </summary>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="type">Notification type filter</param>
        /// <param name="priority">Priority filter</param>
        /// <param name="isRead">Read status filter</param>
        /// <param name="isActionable">Actionable filter</param>
        /// <param name="fromDate">From date filter</param>
        /// <param name="toDate">To date filter</param>
        /// <returns>Paginated list of notifications</returns>
        [HttpGet]
        public async Task<ActionResult<NotificationPagedResult>> GetNotifications(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] NotificationType? type = null,
            [FromQuery] NotificationPriority? priority = null,
            [FromQuery] bool? isRead = null,
            [FromQuery] bool? isActionable = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var searchDto = new NotificationSearchDto
                {
                    Type = type,
                    Priority = priority,
                    IsRead = isRead,
                    IsActionable = isActionable,
                    FromDate = fromDate,
                    ToDate = toDate,
                    Page = page,
                    PageSize = pageSize
                };

                var result = await _notificationService.GetUserNotificationsAsync(currentUserId.Value, searchDto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notifications for user {UserId}", GetCurrentUserId());
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get notification statistics for the current user
        /// </summary>
        /// <returns>Notification statistics</returns>
        [HttpGet("stats")]
        public async Task<ActionResult<NotificationStatsDto>> GetNotificationStats()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var stats = await _notificationService.GetUserNotificationStatsAsync(currentUserId.Value);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notification stats for user {UserId}", GetCurrentUserId());
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Mark specific notification as read
        /// </summary>
        /// <param name="id">Notification ID</param>
        /// <returns>Success confirmation</returns>
        [HttpPut("{id}/read")]
        public async Task<ActionResult> MarkNotificationAsRead(Guid id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var success = await _notificationService.MarkNotificationAsReadAsync(id, currentUserId.Value);
                
                if (!success)
                {
                    return NotFound(new { error = "Notification not found" });
                }

                _logger.LogInformation("Notification marked as read: {NotificationId} by {UserId}", id, currentUserId);
                
                return Ok(new { message = "Notification marked as read" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification {NotificationId} as read", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Mark multiple notifications as read
        /// </summary>
        /// <param name="markReadDto">Mark read parameters</param>
        /// <returns>Number of notifications marked as read</returns>
        [HttpPost("mark-read")]
        public async Task<ActionResult<object>> MarkNotificationsAsRead([FromBody] MarkNotificationsReadDto markReadDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var count = await _notificationService.MarkNotificationsAsReadAsync(markReadDto, currentUserId.Value);
                
                _logger.LogInformation("{Count} notifications marked as read by {UserId}", count, currentUserId);
                
                return Ok(new { message = $"{count} notifications marked as read", count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notifications as read for user {UserId}", GetCurrentUserId());
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Delete a specific notification
        /// </summary>
        /// <param name="id">Notification ID</param>
        /// <returns>Deletion confirmation</returns>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteNotification(Guid id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var success = await _notificationService.DeleteNotificationAsync(id, currentUserId.Value);
                
                if (!success)
                {
                    return NotFound(new { error = "Notification not found" });
                }

                _logger.LogInformation("Notification deleted: {NotificationId} by {UserId}", id, currentUserId);
                
                return Ok(new { message = "Notification deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting notification {NotificationId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Create a new notification (for testing/admin purposes)
        /// </summary>
        /// <param name="createNotificationDto">Notification data</param>
        /// <returns>Created notification</returns>
        [HttpPost]
        public async Task<ActionResult<NotificationDto>> CreateNotification([FromBody] CreateNotificationDto createNotificationDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                // For now, allow users to create notifications for themselves only
                // In a real application, you might want to restrict this to admin users
                if (createNotificationDto.UserId != currentUserId)
                {
                    return Forbid();
                }

                var notification = await _notificationService.CreateNotificationAsync(
                    createNotificationDto.UserId,
                    createNotificationDto.Title,
                    createNotificationDto.Message,
                    createNotificationDto.Type,
                    createNotificationDto.Priority,
                    createNotificationDto.IsActionable,
                    createNotificationDto.ActionType,
                    createNotificationDto.ActionData,
                    createNotificationDto.RelatedMessageId,
                    createNotificationDto.RelatedChatRoomId,
                    createNotificationDto.RelatedUserId,
                    createNotificationDto.ExpiresAt
                );

                if (notification == null)
                {
                    return BadRequest(new { error = "Notification not created due to user settings" });
                }

                _logger.LogInformation("Notification created: {NotificationId} for {UserId}",
                    notification.Id, createNotificationDto.UserId);

                return CreatedAtAction(nameof(GetNotifications), notification);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notification");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get user notification settings
        /// </summary>
        /// <returns>User notification settings</returns>
        [HttpGet("settings")]
        public async Task<ActionResult<NotificationSettingsDto>> GetNotificationSettings()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var settings = await _notificationService.GetUserNotificationSettingsAsync(currentUserId.Value);
                return Ok(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notification settings for user {UserId}", GetCurrentUserId());
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Update user notification settings
        /// </summary>
        /// <param name="updateSettingsDto">Settings update data</param>
        /// <returns>Updated settings</returns>
        [HttpPut("settings")]
        public async Task<ActionResult<NotificationSettingsDto>> UpdateNotificationSettings(
            [FromBody] UpdateNotificationSettingsDto updateSettingsDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var settings = await _notificationService.UpdateUserNotificationSettingsAsync(
                    currentUserId.Value, updateSettingsDto);

                _logger.LogInformation("Notification settings updated for user {UserId}", currentUserId);

                return Ok(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating notification settings for user {UserId}", GetCurrentUserId());
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Reset notification settings to default values
        /// </summary>
        /// <returns>Reset settings</returns>
        [HttpPost("settings/reset")]
        public async Task<ActionResult<NotificationSettingsDto>> ResetNotificationSettings()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var settings = await _notificationService.ResetUserNotificationSettingsAsync(currentUserId.Value);

                _logger.LogInformation("Notification settings reset for user {UserId}", currentUserId);

                return Ok(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting notification settings for user {UserId}", GetCurrentUserId());
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Send a test notification to the current user
        /// </summary>
        /// <param name="testDto">Test notification parameters</param>
        /// <returns>Test notification result</returns>
        [HttpPost("test")]
        public async Task<ActionResult<NotificationDto>> SendTestNotification(
            [FromBody] TestNotificationDto testDto)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var notification = await _notificationService.CreateNotificationAsync(
                    currentUserId.Value,
                    testDto.Title ?? "Test Notification",
                    testDto.Message ?? "This is a test notification to verify your notification settings.",
                    testDto.Type ?? NotificationType.Info,
                    testDto.Priority ?? NotificationPriority.Normal,
                    false
                );

                if (notification == null)
                {
                    return BadRequest(new { error = "Test notification not sent due to user settings" });
                }

                _logger.LogInformation("Test notification sent to user {UserId}", currentUserId);

                return Ok(notification);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending test notification to user {UserId}", GetCurrentUserId());
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get unread notification count
        /// </summary>
        /// <returns>Unread notification count</returns>
        [HttpGet("unread-count")]
        public async Task<ActionResult<object>> GetUnreadCount()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var stats = await _notificationService.GetUserNotificationStatsAsync(currentUserId.Value);
                
                return Ok(new 
                { 
                    unreadCount = stats.UnreadNotifications,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting unread count for user {UserId}", GetCurrentUserId());
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get notifications by type
        /// </summary>
        /// <param name="type">Notification type</param>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <returns>Notifications of the specified type</returns>
        [HttpGet("by-type/{type}")]
        public async Task<ActionResult<NotificationPagedResult>> GetNotificationsByType(
            NotificationType type,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var searchDto = new NotificationSearchDto
                {
                    Type = type,
                    Page = page,
                    PageSize = pageSize
                };

                var result = await _notificationService.GetUserNotificationsAsync(currentUserId.Value, searchDto);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notifications by type {Type} for user {UserId}", 
                    type, GetCurrentUserId());
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Clear all notifications for the current user
        /// </summary>
        /// <returns>Clear confirmation</returns>
        [HttpDelete("clear-all")]
        public async Task<ActionResult> ClearAllNotifications()
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                // Mark all as read instead of deleting (better UX)
                var markReadDto = new MarkNotificationsReadDto
                {
                    MarkAllAsRead = true
                };

                var count = await _notificationService.MarkNotificationsAsReadAsync(markReadDto, currentUserId.Value);

                _logger.LogInformation("All notifications cleared for user {UserId}, {Count} notifications marked as read", 
                    currentUserId, count);

                return Ok(new { message = $"All notifications cleared. {count} notifications marked as read.", count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all notifications for user {UserId}", GetCurrentUserId());
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            return userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId) ? userId : null;
        }
    }

    /// <summary>
    /// DTO for testing notifications
    /// </summary>
    public class TestNotificationDto
    {
        public string? Title { get; set; }
        public string? Message { get; set; }
        public NotificationType? Type { get; set; }
        public NotificationPriority? Priority { get; set; }
    }
} 