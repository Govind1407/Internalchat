using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ChatAPI.Data;
using ChatAPI.Models;
using ChatAPI.Services;

namespace ChatAPI.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ChatDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(
            ChatDbContext context,
            INotificationService notificationService,
            ILogger<ChatHub> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    _logger.LogWarning("User attempted to connect without valid authentication");
                    await Clients.Caller.SendAsync("Error", "Authentication required");
                    Context.Abort();
                    return;
                }

                // Get user information
                var user = await _context.Users.FindAsync(userId.Value);
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found in database", userId.Value);
                    await Clients.Caller.SendAsync("Error", "User not found");
                    Context.Abort();
                    return;
                }

                // Store connection
                var connection = new UserConnection
                {
                    UserId = userId.Value,
                    ConnectionId = Context.ConnectionId,
                    DeviceType = GetDeviceType(),
                    UserAgent = GetUserAgent(),
                    ConnectedAt = DateTime.UtcNow
                };

                _context.UserConnections.Add(connection);

                // Update user online status
                user.IsOnline = true;
                user.LastSeen = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Join default chat room
                var defaultChatRoom = await _context.ChatRooms
                    .FirstOrDefaultAsync(cr => cr.Name == "General" && cr.Type == ChatRoomType.Public);

                if (defaultChatRoom != null)
                {
                    await JoinChatRoom(defaultChatRoom.Id.ToString());
                }

                // Get user's chat rooms
                var userChatRooms = await _context.UserChatRooms
                    .Where(ucr => ucr.UserId == userId.Value && ucr.LeftAt == null)
                    .Include(ucr => ucr.ChatRoom)
                    .Select(ucr => ucr.ChatRoom)
                    .ToListAsync();

                // Join all user's chat rooms
                foreach (var chatRoom in userChatRooms)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"ChatRoom_{chatRoom.Id}");
                }

                // Notify others that user is online
                await Clients.Others.SendAsync("UserOnline", new
                {
                    UserId = user.Id,
                    Username = user.Username,
                    Avatar = user.Avatar,
                    Timestamp = DateTime.UtcNow
                });

                // Send online users to the new connection
                var onlineUsers = await GetOnlineUsers();
                await Clients.Caller.SendAsync("OnlineUsers", onlineUsers);

                _logger.LogInformation("User {Username} ({UserId}) connected with connection {ConnectionId}",
                    user.Username, userId.Value, Context.ConnectionId);

                await base.OnConnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnConnectedAsync for connection {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("Error", "Connection failed");
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId.HasValue)
                {
                    // Remove connection
                    var connection = await _context.UserConnections
                        .FirstOrDefaultAsync(c => c.ConnectionId == Context.ConnectionId);

                    if (connection != null)
                    {
                        _context.UserConnections.Remove(connection);
                    }

                    // Check if user has other connections
                    var hasOtherConnections = await _context.UserConnections
                        .AnyAsync(c => c.UserId == userId.Value && c.ConnectionId != Context.ConnectionId);

                    // Update user status if no other connections
                    if (!hasOtherConnections)
                    {
                        var user = await _context.Users.FindAsync(userId.Value);
                        if (user != null)
                        {
                            user.IsOnline = false;
                            user.LastSeen = DateTime.UtcNow;

                            // Notify others that user is offline
                            await Clients.Others.SendAsync("UserOffline", new
                            {
                                UserId = user.Id,
                                Username = user.Username,
                                Avatar = user.Avatar,
                                Timestamp = DateTime.UtcNow
                            });

                            _logger.LogInformation("User {Username} ({UserId}) went offline",
                                user.Username, userId.Value);
                        }
                    }

                    await _context.SaveChangesAsync();
                }

                if (exception != null)
                {
                    _logger.LogError(exception, "User disconnected with error for connection {ConnectionId}", Context.ConnectionId);
                }
                else
                {
                    _logger.LogInformation("User disconnected normally for connection {ConnectionId}", Context.ConnectionId);
                }

                await base.OnDisconnectedAsync(exception);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnDisconnectedAsync for connection {ConnectionId}", Context.ConnectionId);
            }
        }

        public async Task JoinChatRoom(string chatRoomId)
        {
            try
            {
                if (!Guid.TryParse(chatRoomId, out var roomId))
                {
                    await Clients.Caller.SendAsync("Error", "Invalid chat room ID");
                    return;
                }

                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                {
                    await Clients.Caller.SendAsync("Error", "Authentication required");
                    return;
                }

                // Check if user is member of the chat room
                var membership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == userId.Value && 
                                              ucr.ChatRoomId == roomId && 
                                              ucr.LeftAt == null);

                if (membership == null)
                {
                    // Auto-join public rooms
                    var chatRoom = await _context.ChatRooms.FindAsync(roomId);
                    if (chatRoom?.Type == ChatRoomType.Public)
                    {
                        var newMembership = new UserChatRoom
                        {
                            UserId = userId.Value,
                            ChatRoomId = roomId,
                            Role = UserRole.Member,
                            JoinedAt = DateTime.UtcNow
                        };

                        _context.UserChatRooms.Add(newMembership);
                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        await Clients.Caller.SendAsync("Error", "Not authorized to join this chat room");
                        return;
                    }
                }

                await Groups.AddToGroupAsync(Context.ConnectionId, $"ChatRoom_{roomId}");
                await Clients.Caller.SendAsync("JoinedChatRoom", chatRoomId);

                _logger.LogInformation("User {UserId} joined chat room {ChatRoomId}",
                    userId.Value, roomId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining chat room {ChatRoomId}", chatRoomId);
                await Clients.Caller.SendAsync("Error", "Failed to join chat room");
            }
        }

        public async Task LeaveChatRoom(string chatRoomId)
        {
            try
            {
                if (!Guid.TryParse(chatRoomId, out var roomId))
                {
                    await Clients.Caller.SendAsync("Error", "Invalid chat room ID");
                    return;
                }

                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ChatRoom_{roomId}");
                await Clients.Caller.SendAsync("LeftChatRoom", chatRoomId);

                _logger.LogInformation("User {UserId} left chat room {ChatRoomId}",
                    GetCurrentUserId(), roomId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving chat room {ChatRoomId}", chatRoomId);
                await Clients.Caller.SendAsync("Error", "Failed to leave chat room");
            }
        }

        public async Task SendMessage(string chatRoomId, string content, string? replyToMessageId = null)
        {
            try
            {
                if (!Guid.TryParse(chatRoomId, out var roomId))
                {
                    await Clients.Caller.SendAsync("Error", "Invalid chat room ID");
                    return;
                }

                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                {
                    await Clients.Caller.SendAsync("Error", "Authentication required");
                    return;
                }

                if (string.IsNullOrWhiteSpace(content) || content.Length > 2000)
                {
                    await Clients.Caller.SendAsync("Error", "Invalid message content");
                    return;
                }

                // Check if user is member of the chat room
                var membership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == userId.Value && 
                                              ucr.ChatRoomId == roomId && 
                                              ucr.LeftAt == null);

                if (membership == null)
                {
                    await Clients.Caller.SendAsync("Error", "Not a member of this chat room");
                    return;
                }

                if (membership.IsMuted)
                {
                    await Clients.Caller.SendAsync("Error", "You are muted in this chat room");
                    return;
                }

                // Get user and chat room info
                var user = await _context.Users.FindAsync(userId.Value);
                var chatRoom = await _context.ChatRooms.FindAsync(roomId);

                if (user == null || chatRoom == null)
                {
                    await Clients.Caller.SendAsync("Error", "User or chat room not found");
                    return;
                }

                // Create message
                var message = new Message
                {
                    UserId = userId.Value,
                    ChatRoomId = roomId,
                    Content = content.Trim(),
                    Type = MessageType.Text,
                    CreatedAt = DateTime.UtcNow
                };

                // Handle reply
                if (!string.IsNullOrEmpty(replyToMessageId) && Guid.TryParse(replyToMessageId, out var replyId))
                {
                    var replyToMessage = await _context.Messages.FindAsync(replyId);
                    if (replyToMessage?.ChatRoomId == roomId)
                    {
                        message.ReplyToMessageId = replyId;
                    }
                }

                _context.Messages.Add(message);
                await _context.SaveChangesAsync();

                // Create message DTO for response
                var messageDto = new MessageDto
                {
                    Id = message.Id,
                    UserId = message.UserId,
                    ChatRoomId = message.ChatRoomId,
                    Content = message.Content,
                    Type = message.Type,
                    CreatedAt = message.CreatedAt,
                    ReplyToMessageId = message.ReplyToMessageId,
                    User = new UserDto
                    {
                        Id = user.Id,
                        Username = user.Username,
                        Avatar = user.Avatar,
                        IsOnline = user.IsOnline
                    }
                };

                // Send message to all users in the chat room
                await Clients.Group($"ChatRoom_{roomId}").SendAsync("NewMessage", messageDto);

                // Handle mentions and create notifications
                await HandleMessageMentions(message, content, user);

                _logger.LogInformation("Message sent by {Username} in chat room {ChatRoomId}",
                    user.Username, roomId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to chat room {ChatRoomId}", chatRoomId);
                await Clients.Caller.SendAsync("Error", "Failed to send message");
            }
        }

        public async Task StartTyping(string chatRoomId)
        {
            try
            {
                if (!Guid.TryParse(chatRoomId, out var roomId))
                    return;

                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                    return;

                var user = await _context.Users.FindAsync(userId.Value);
                if (user == null)
                    return;

                var typingIndicator = new TypingIndicatorDto
                {
                    ChatRoomId = roomId,
                    UserId = userId.Value,
                    Username = user.Username,
                    IsTyping = true,
                    Timestamp = DateTime.UtcNow
                };

                await Clients.GroupExcept($"ChatRoom_{roomId}", Context.ConnectionId)
                    .SendAsync("UserTyping", typingIndicator);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in StartTyping for chat room {ChatRoomId}", chatRoomId);
            }
        }

        public async Task StopTyping(string chatRoomId)
        {
            try
            {
                if (!Guid.TryParse(chatRoomId, out var roomId))
                    return;

                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                    return;

                var user = await _context.Users.FindAsync(userId.Value);
                if (user == null)
                    return;

                var typingIndicator = new TypingIndicatorDto
                {
                    ChatRoomId = roomId,
                    UserId = userId.Value,
                    Username = user.Username,
                    IsTyping = false,
                    Timestamp = DateTime.UtcNow
                };

                await Clients.GroupExcept($"ChatRoom_{roomId}", Context.ConnectionId)
                    .SendAsync("UserTyping", typingIndicator);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in StopTyping for chat room {ChatRoomId}", chatRoomId);
            }
        }

        private async Task HandleMessageMentions(Message message, string content, User sender)
        {
            try
            {
                // Simple mention detection (@username)
                var mentionPattern = @"@(\w+)";
                var matches = System.Text.RegularExpressions.Regex.Matches(content, mentionPattern);

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var username = match.Groups[1].Value;
                    var mentionedUser = await _context.Users
                        .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

                    if (mentionedUser != null && mentionedUser.Id != sender.Id)
                    {
                        // Create mention record
                        var mention = new MessageMention
                        {
                            MessageId = message.Id,
                            MentionedUserId = mentionedUser.Id,
                            StartIndex = match.Index,
                            Length = match.Length
                        };

                        _context.MessageMentions.Add(mention);

                        // Create notification
                        await _notificationService.CreateNotificationAsync(
                            mentionedUser.Id,
                            $"{sender.Username} mentioned you",
                            content.Length > 100 ? content.Substring(0, 100) + "..." : content,
                            NotificationType.Mention,
                            NotificationPriority.High,
                            relatedMessageId: message.Id,
                            relatedChatRoomId: message.ChatRoomId,
                            relatedUserId: sender.Id
                        );

                        // Send real-time notification
                        var userConnections = await _context.UserConnections
                            .Where(c => c.UserId == mentionedUser.Id)
                            .Select(c => c.ConnectionId)
                            .ToListAsync();

                        if (userConnections.Any())
                        {
                            await Clients.Clients(userConnections).SendAsync("Mention", new
                            {
                                MessageId = message.Id,
                                ChatRoomId = message.ChatRoomId,
                                MentionedBy = sender.Username,
                                Content = content,
                                Timestamp = DateTime.UtcNow
                            });
                        }
                    }
                }

                if (matches.Count > 0)
                {
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling mentions for message {MessageId}", message.Id);
            }
        }

        private async Task<List<object>> GetOnlineUsers()
        {
            return await _context.Users
                .Where(u => u.IsOnline)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.Avatar,
                    u.LastSeen
                })
                .Cast<object>()
                .ToListAsync();
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier);
            return userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId) ? userId : null;
        }

        private string? GetDeviceType()
        {
            var userAgent = Context.GetHttpContext()?.Request.Headers["User-Agent"].ToString();
            if (string.IsNullOrEmpty(userAgent))
                return null;

            if (userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase))
                return "Mobile";
            if (userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase))
                return "Tablet";
            return "Desktop";
        }

        private string? GetUserAgent()
        {
            return Context.GetHttpContext()?.Request.Headers["User-Agent"].ToString();
        }
    }
} 