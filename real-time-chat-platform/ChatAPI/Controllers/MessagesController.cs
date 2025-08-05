using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ChatAPI.Data;
using ChatAPI.Models;

namespace ChatAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MessagesController : ControllerBase
    {
        private readonly ChatDbContext _context;
        private readonly ILogger<MessagesController> _logger;

        public MessagesController(ChatDbContext context, ILogger<MessagesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get messages with pagination and filtering
        /// </summary>
        /// <param name="chatRoomId">Chat room ID filter</param>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="search">Search query</param>
        /// <param name="fromDate">From date filter</param>
        /// <param name="toDate">To date filter</param>
        /// <param name="userId">User ID filter</param>
        /// <returns>Paginated list of messages</returns>
        [HttpGet]
        public async Task<ActionResult<MessagePagedResult>> GetMessages(
            [FromQuery] Guid? chatRoomId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] Guid? userId = null)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                page = Math.Max(1, page);
                pageSize = Math.Min(100, Math.Max(1, pageSize));

                var query = _context.Messages
                    .Include(m => m.User)
                    .Include(m => m.ChatRoom)
                    .Include(m => m.ReplyToMessage)
                        .ThenInclude(rm => rm!.User)
                    .Include(m => m.Reactions)
                        .ThenInclude(r => r.User)
                    .Include(m => m.Mentions)
                        .ThenInclude(men => men.MentionedUser)
                    .Where(m => !m.IsDeleted)
                    .AsQueryable();

                // Filter by chat room
                if (chatRoomId.HasValue)
                {
                    // Check if user has access to the chat room
                    var userMembership = await _context.UserChatRooms
                        .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && 
                                                  ucr.ChatRoomId == chatRoomId && 
                                                  ucr.LeftAt == null);

                    var chatRoom = await _context.ChatRooms.FindAsync(chatRoomId);
                    if (chatRoom == null)
                    {
                        return NotFound(new { error = "Chat room not found" });
                    }

                    if (chatRoom.Type != ChatRoomType.Public && userMembership == null)
                    {
                        return Forbid();
                    }

                    query = query.Where(m => m.ChatRoomId == chatRoomId);
                }
                else
                {
                    // Only show messages from chat rooms the user is a member of
                    var userChatRoomIds = await _context.UserChatRooms
                        .Where(ucr => ucr.UserId == currentUserId && ucr.LeftAt == null)
                        .Select(ucr => ucr.ChatRoomId)
                        .ToListAsync();

                    query = query.Where(m => userChatRoomIds.Contains(m.ChatRoomId));
                }

                // Apply other filters
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(m => m.Content.Contains(search));
                }

                if (fromDate.HasValue)
                {
                    query = query.Where(m => m.CreatedAt >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(m => m.CreatedAt <= toDate.Value);
                }

                if (userId.HasValue)
                {
                    query = query.Where(m => m.UserId == userId.Value);
                }

                // Order by creation date (newest first)
                query = query.OrderByDescending(m => m.CreatedAt);

                var totalCount = await query.CountAsync();
                var messages = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var messageDtos = messages.Select(MapToMessageDto).ToList();

                var result = new MessagePagedResult
                {
                    Messages = messageDtos,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                    HasNextPage = page * pageSize < totalCount,
                    HasPreviousPage = page > 1
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get message by ID
        /// </summary>
        /// <param name="id">Message ID</param>
        /// <returns>Message details</returns>
        [HttpGet("{id}")]
        public async Task<ActionResult<MessageDto>> GetMessage(Guid id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var message = await _context.Messages
                    .Include(m => m.User)
                    .Include(m => m.ChatRoom)
                    .Include(m => m.ReplyToMessage)
                        .ThenInclude(rm => rm!.User)
                    .Include(m => m.Reactions)
                        .ThenInclude(r => r.User)
                    .Include(m => m.Mentions)
                        .ThenInclude(men => men.MentionedUser)
                    .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);

                if (message == null)
                {
                    return NotFound(new { error = "Message not found" });
                }

                // Check if user has access to the chat room
                var userMembership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && 
                                              ucr.ChatRoomId == message.ChatRoomId && 
                                              ucr.LeftAt == null);

                if (message.ChatRoom.Type != ChatRoomType.Public && userMembership == null)
                {
                    return Forbid();
                }

                var messageDto = MapToMessageDto(message);
                return Ok(messageDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting message {MessageId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Send a new message
        /// </summary>
        /// <param name="chatRoomId">Chat room ID</param>
        /// <param name="createMessageDto">Message data</param>
        /// <returns>Created message</returns>
        [HttpPost]
        public async Task<ActionResult<MessageDto>> CreateMessage(
            [FromQuery] Guid chatRoomId,
            [FromBody] CreateMessageDto createMessageDto)
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

                // Check if chat room exists and user has access
                var chatRoom = await _context.ChatRooms.FindAsync(chatRoomId);
                if (chatRoom == null)
                {
                    return NotFound(new { error = "Chat room not found" });
                }

                var userMembership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && 
                                              ucr.ChatRoomId == chatRoomId && 
                                              ucr.LeftAt == null);

                if (userMembership == null)
                {
                    return Forbid();
                }

                if (userMembership.IsMuted)
                {
                    return BadRequest(new { error = "You are muted in this chat room" });
                }

                // Validate reply-to message if provided
                Message? replyToMessage = null;
                if (createMessageDto.ReplyToMessageId.HasValue)
                {
                    replyToMessage = await _context.Messages
                        .FirstOrDefaultAsync(m => m.Id == createMessageDto.ReplyToMessageId.Value && 
                                                m.ChatRoomId == chatRoomId && 
                                                !m.IsDeleted);

                    if (replyToMessage == null)
                    {
                        return BadRequest(new { error = "Reply-to message not found" });
                    }
                }

                var message = new Message
                {
                    UserId = currentUserId.Value,
                    ChatRoomId = chatRoomId,
                    Content = createMessageDto.Content.Trim(),
                    Type = createMessageDto.Type,
                    AttachmentUrl = createMessageDto.AttachmentUrl,
                    AttachmentName = createMessageDto.AttachmentName,
                    AttachmentSize = createMessageDto.AttachmentSize,
                    ReplyToMessageId = createMessageDto.ReplyToMessageId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Messages.Add(message);
                await _context.SaveChangesAsync();

                // Load the complete message for response
                var createdMessage = await _context.Messages
                    .Include(m => m.User)
                    .Include(m => m.ChatRoom)
                    .Include(m => m.ReplyToMessage)
                        .ThenInclude(rm => rm!.User)
                    .Include(m => m.Reactions)
                        .ThenInclude(r => r.User)
                    .Include(m => m.Mentions)
                        .ThenInclude(men => men.MentionedUser)
                    .FirstAsync(m => m.Id == message.Id);

                var messageDto = MapToMessageDto(createdMessage);

                _logger.LogInformation("Message created: {MessageId} by {UserId} in {ChatRoomId}",
                    message.Id, currentUserId, chatRoomId);

                return CreatedAtAction(nameof(GetMessage), new { id = message.Id }, messageDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating message");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Update a message
        /// </summary>
        /// <param name="id">Message ID</param>
        /// <param name="updateMessageDto">Update data</param>
        /// <returns>Updated message</returns>
        [HttpPut("{id}")]
        public async Task<ActionResult<MessageDto>> UpdateMessage(Guid id, [FromBody] UpdateMessageDto updateMessageDto)
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

                var message = await _context.Messages
                    .Include(m => m.User)
                    .Include(m => m.ChatRoom)
                    .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);

                if (message == null)
                {
                    return NotFound(new { error = "Message not found" });
                }

                // Only the message author can edit their message
                if (message.UserId != currentUserId)
                {
                    return Forbid();
                }

                // Check if message is too old to edit (24 hours)
                if (DateTime.UtcNow.Subtract(message.CreatedAt).TotalHours > 24)
                {
                    return BadRequest(new { error = "Message is too old to edit" });
                }

                message.Content = updateMessageDto.Content.Trim();
                message.IsEdited = true;
                message.EditedAt = DateTime.UtcNow;
                message.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Load the updated message with all related data
                var updatedMessage = await _context.Messages
                    .Include(m => m.User)
                    .Include(m => m.ChatRoom)
                    .Include(m => m.ReplyToMessage)
                        .ThenInclude(rm => rm!.User)
                    .Include(m => m.Reactions)
                        .ThenInclude(r => r.User)
                    .Include(m => m.Mentions)
                        .ThenInclude(men => men.MentionedUser)
                    .FirstAsync(m => m.Id == id);

                var messageDto = MapToMessageDto(updatedMessage);

                _logger.LogInformation("Message updated: {MessageId} by {UserId}", id, currentUserId);

                return Ok(messageDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating message {MessageId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Delete a message
        /// </summary>
        /// <param name="id">Message ID</param>
        /// <returns>Deletion confirmation</returns>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteMessage(Guid id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var message = await _context.Messages
                    .Include(m => m.ChatRoom)
                    .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);

                if (message == null)
                {
                    return NotFound(new { error = "Message not found" });
                }

                // Check permissions - message author or chat room admin/owner can delete
                var canDelete = message.UserId == currentUserId;
                
                if (!canDelete)
                {
                    var userMembership = await _context.UserChatRooms
                        .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && 
                                                  ucr.ChatRoomId == message.ChatRoomId && 
                                                  ucr.LeftAt == null);

                    canDelete = userMembership != null && 
                               (userMembership.Role == UserRole.Owner || 
                                userMembership.Role == UserRole.Admin || 
                                userMembership.Role == UserRole.Moderator);
                }

                if (!canDelete)
                {
                    return Forbid();
                }

                // Soft delete
                message.IsDeleted = true;
                message.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Message deleted: {MessageId} by {UserId}", id, currentUserId);

                return Ok(new { message = "Message deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting message {MessageId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Add reaction to a message
        /// </summary>
        /// <param name="id">Message ID</param>
        /// <param name="createReactionDto">Reaction data</param>
        /// <returns>Created reaction</returns>
        [HttpPost("{id}/reactions")]
        public async Task<ActionResult<MessageReactionDto>> AddReaction(
            Guid id, 
            [FromBody] CreateMessageReactionDto createReactionDto)
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

                var message = await _context.Messages
                    .Include(m => m.ChatRoom)
                    .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);

                if (message == null)
                {
                    return NotFound(new { error = "Message not found" });
                }

                // Check if user has access to the chat room
                var userMembership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && 
                                              ucr.ChatRoomId == message.ChatRoomId && 
                                              ucr.LeftAt == null);

                if (message.ChatRoom.Type != ChatRoomType.Public && userMembership == null)
                {
                    return Forbid();
                }

                // Check if user already reacted with this emoji
                var existingReaction = await _context.MessageReactions
                    .FirstOrDefaultAsync(mr => mr.MessageId == id && 
                                             mr.UserId == currentUserId && 
                                             mr.Emoji == createReactionDto.Emoji);

                if (existingReaction != null)
                {
                    return BadRequest(new { error = "You have already reacted with this emoji" });
                }

                var reaction = new MessageReaction
                {
                    MessageId = id,
                    UserId = currentUserId.Value,
                    Emoji = createReactionDto.Emoji,
                    CreatedAt = DateTime.UtcNow
                };

                _context.MessageReactions.Add(reaction);
                await _context.SaveChangesAsync();

                // Load the reaction with user data
                var createdReaction = await _context.MessageReactions
                    .Include(mr => mr.User)
                    .FirstAsync(mr => mr.Id == reaction.Id);

                var reactionDto = new MessageReactionDto
                {
                    Id = createdReaction.Id,
                    MessageId = createdReaction.MessageId,
                    UserId = createdReaction.UserId,
                    Emoji = createdReaction.Emoji,
                    CreatedAt = createdReaction.CreatedAt,
                    User = new UserDto
                    {
                        Id = createdReaction.User.Id,
                        Username = createdReaction.User.Username,
                        Avatar = createdReaction.User.Avatar
                    }
                };

                _logger.LogInformation("Reaction added: {ReactionId} by {UserId} to message {MessageId}",
                    reaction.Id, currentUserId, id);

                return CreatedAtAction(nameof(GetMessage), new { id }, reactionDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding reaction to message {MessageId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Remove reaction from a message
        /// </summary>
        /// <param name="id">Message ID</param>
        /// <param name="reactionId">Reaction ID</param>
        /// <returns>Removal confirmation</returns>
        [HttpDelete("{id}/reactions/{reactionId}")]
        public async Task<ActionResult> RemoveReaction(Guid id, Guid reactionId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var reaction = await _context.MessageReactions
                    .Include(mr => mr.Message)
                        .ThenInclude(m => m.ChatRoom)
                    .FirstOrDefaultAsync(mr => mr.Id == reactionId && mr.MessageId == id);

                if (reaction == null)
                {
                    return NotFound(new { error = "Reaction not found" });
                }

                // Only the reaction author can remove their reaction
                if (reaction.UserId != currentUserId)
                {
                    return Forbid();
                }

                _context.MessageReactions.Remove(reaction);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Reaction removed: {ReactionId} by {UserId} from message {MessageId}",
                    reactionId, currentUserId, id);

                return Ok(new { message = "Reaction removed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing reaction {ReactionId} from message {MessageId}", reactionId, id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Search messages
        /// </summary>
        /// <param name="searchDto">Search parameters</param>
        /// <returns>Search results</returns>
        [HttpPost("search")]
        public async Task<ActionResult<MessagePagedResult>> SearchMessages([FromBody] MessageSearchDto searchDto)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                if (string.IsNullOrEmpty(searchDto.Query) || searchDto.Query.Length < 2)
                {
                    return BadRequest(new { error = "Search query must be at least 2 characters long" });
                }

                searchDto.Page = Math.Max(1, searchDto.Page);
                searchDto.PageSize = Math.Min(100, Math.Max(1, searchDto.PageSize));

                var query = _context.Messages
                    .Include(m => m.User)
                    .Include(m => m.ChatRoom)
                    .Include(m => m.ReplyToMessage)
                        .ThenInclude(rm => rm!.User)
                    .Include(m => m.Reactions)
                        .ThenInclude(r => r.User)
                    .Include(m => m.Mentions)
                        .ThenInclude(men => men.MentionedUser)
                    .Where(m => !m.IsDeleted)
                    .AsQueryable();

                // Only search in chat rooms the user is a member of
                if (searchDto.ChatRoomId.HasValue)
                {
                    // Check access to specific chat room
                    var userMembership = await _context.UserChatRooms
                        .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && 
                                                  ucr.ChatRoomId == searchDto.ChatRoomId && 
                                                  ucr.LeftAt == null);

                    var chatRoom = await _context.ChatRooms.FindAsync(searchDto.ChatRoomId);
                    if (chatRoom == null)
                    {
                        return NotFound(new { error = "Chat room not found" });
                    }

                    if (chatRoom.Type != ChatRoomType.Public && userMembership == null)
                    {
                        return Forbid();
                    }

                    query = query.Where(m => m.ChatRoomId == searchDto.ChatRoomId);
                }
                else
                {
                    var userChatRoomIds = await _context.UserChatRooms
                        .Where(ucr => ucr.UserId == currentUserId && ucr.LeftAt == null)
                        .Select(ucr => ucr.ChatRoomId)
                        .ToListAsync();

                    query = query.Where(m => userChatRoomIds.Contains(m.ChatRoomId));
                }

                // Apply search filters
                query = query.Where(m => m.Content.Contains(searchDto.Query));

                if (searchDto.UserId.HasValue)
                {
                    query = query.Where(m => m.UserId == searchDto.UserId.Value);
                }

                if (searchDto.Type.HasValue)
                {
                    query = query.Where(m => m.Type == searchDto.Type.Value);
                }

                if (searchDto.FromDate.HasValue)
                {
                    query = query.Where(m => m.CreatedAt >= searchDto.FromDate.Value);
                }

                if (searchDto.ToDate.HasValue)
                {
                    query = query.Where(m => m.CreatedAt <= searchDto.ToDate.Value);
                }

                // Order by relevance (exact matches first, then by date)
                query = query.OrderByDescending(m => m.Content.StartsWith(searchDto.Query))
                           .ThenByDescending(m => m.CreatedAt);

                var totalCount = await query.CountAsync();
                var messages = await query
                    .Skip((searchDto.Page - 1) * searchDto.PageSize)
                    .Take(searchDto.PageSize)
                    .ToListAsync();

                var messageDtos = messages.Select(MapToMessageDto).ToList();

                var result = new MessagePagedResult
                {
                    Messages = messageDtos,
                    TotalCount = totalCount,
                    Page = searchDto.Page,
                    PageSize = searchDto.PageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / searchDto.PageSize),
                    HasNextPage = searchDto.Page * searchDto.PageSize < totalCount,
                    HasPreviousPage = searchDto.Page > 1
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching messages");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        private static MessageDto MapToMessageDto(Message message)
        {
            return new MessageDto
            {
                Id = message.Id,
                UserId = message.UserId,
                ChatRoomId = message.ChatRoomId,
                Content = message.Content,
                Type = message.Type,
                AttachmentUrl = message.AttachmentUrl,
                AttachmentName = message.AttachmentName,
                AttachmentSize = message.AttachmentSize,
                IsEdited = message.IsEdited,
                EditedAt = message.EditedAt,
                CreatedAt = message.CreatedAt,
                ReplyToMessageId = message.ReplyToMessageId,
                User = new UserDto
                {
                    Id = message.User.Id,
                    Username = message.User.Username,
                    Email = message.User.Email,
                    Avatar = message.User.Avatar,
                    IsOnline = message.User.IsOnline,
                    LastSeen = message.User.LastSeen,
                    CreatedAt = message.User.CreatedAt
                },
                ReplyToMessage = message.ReplyToMessage != null ? new MessageDto
                {
                    Id = message.ReplyToMessage.Id,
                    Content = message.ReplyToMessage.Content,
                    Type = message.ReplyToMessage.Type,
                    CreatedAt = message.ReplyToMessage.CreatedAt,
                    User = new UserDto
                    {
                        Id = message.ReplyToMessage.User.Id,
                        Username = message.ReplyToMessage.User.Username,
                        Avatar = message.ReplyToMessage.User.Avatar
                    }
                } : null,
                Reactions = message.Reactions.Select(r => new MessageReactionDto
                {
                    Id = r.Id,
                    MessageId = r.MessageId,
                    UserId = r.UserId,
                    Emoji = r.Emoji,
                    CreatedAt = r.CreatedAt,
                    User = new UserDto
                    {
                        Id = r.User.Id,
                        Username = r.User.Username,
                        Avatar = r.User.Avatar
                    }
                }).ToList(),
                Mentions = message.Mentions.Select(m => new MessageMentionDto
                {
                    Id = m.Id,
                    MessageId = m.MessageId,
                    MentionedUserId = m.MentionedUserId,
                    StartIndex = m.StartIndex,
                    Length = m.Length,
                    MentionedUser = new UserDto
                    {
                        Id = m.MentionedUser.Id,
                        Username = m.MentionedUser.Username,
                        Avatar = m.MentionedUser.Avatar
                    }
                }).ToList()
            };
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            return userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId) ? userId : null;
        }
    }
} 