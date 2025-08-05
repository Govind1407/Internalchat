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
    public class ChatRoomsController : ControllerBase
    {
        private readonly ChatDbContext _context;
        private readonly ILogger<ChatRoomsController> _logger;

        public ChatRoomsController(ChatDbContext context, ILogger<ChatRoomsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get chat rooms with pagination and filtering
        /// </summary>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="search">Search query</param>
        /// <param name="type">Chat room type filter</param>
        /// <param name="userRoomsOnly">Show only rooms the current user is a member of</param>
        /// <returns>Paginated list of chat rooms</returns>
        [HttpGet]
        public async Task<ActionResult<ChatRoomPagedResult>> GetChatRooms(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] ChatRoomType? type = null,
            [FromQuery] bool userRoomsOnly = false)
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

                var query = _context.ChatRooms
                    .Include(cr => cr.CreatedByUser)
                    .AsQueryable();

                // Filter by user membership if requested
                if (userRoomsOnly)
                {
                    query = query.Where(cr => cr.UserChatRooms.Any(ucr => ucr.UserId == currentUserId && ucr.LeftAt == null));
                }
                else
                {
                    // Only show public rooms or rooms the user is a member of
                    query = query.Where(cr => cr.Type == ChatRoomType.Public || 
                                            cr.UserChatRooms.Any(ucr => ucr.UserId == currentUserId && ucr.LeftAt == null));
                }

                // Apply search filter
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(cr => cr.Name.Contains(search) || 
                                            (cr.Description != null && cr.Description.Contains(search)));
                }

                // Apply type filter
                if (type.HasValue)
                {
                    query = query.Where(cr => cr.Type == type.Value);
                }

                // Filter active rooms
                query = query.Where(cr => cr.IsActive);

                // Order by name
                query = query.OrderBy(cr => cr.Name);

                var totalCount = await query.CountAsync();
                var chatRooms = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var chatRoomDtos = new List<ChatRoomDto>();
                foreach (var chatRoom in chatRooms)
                {
                    var memberCount = await _context.UserChatRooms
                        .CountAsync(ucr => ucr.ChatRoomId == chatRoom.Id && ucr.LeftAt == null);

                    var userMembership = await _context.UserChatRooms
                        .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && ucr.ChatRoomId == chatRoom.Id && ucr.LeftAt == null);

                    var lastMessage = await _context.Messages
                        .Where(m => m.ChatRoomId == chatRoom.Id)
                        .Include(m => m.User)
                        .OrderByDescending(m => m.CreatedAt)
                        .FirstOrDefaultAsync();

                    var unreadCount = 0;
                    if (userMembership != null)
                    {
                        var lastReadTime = userMembership.LastReadMessageAt ?? userMembership.JoinedAt;
                        unreadCount = await _context.Messages
                            .CountAsync(m => m.ChatRoomId == chatRoom.Id && m.CreatedAt > lastReadTime);
                    }

                    chatRoomDtos.Add(new ChatRoomDto
                    {
                        Id = chatRoom.Id,
                        Name = chatRoom.Name,
                        Description = chatRoom.Description,
                        Type = chatRoom.Type,
                        Avatar = chatRoom.Avatar,
                        IsActive = chatRoom.IsActive,
                        AllowFileUploads = chatRoom.AllowFileUploads,
                        MaxMembers = chatRoom.MaxMembers,
                        CurrentMemberCount = memberCount,
                        CreatedAt = chatRoom.CreatedAt,
                        CreatedByUser = chatRoom.CreatedByUser != null ? new UserDto
                        {
                            Id = chatRoom.CreatedByUser.Id,
                            Username = chatRoom.CreatedByUser.Username,
                            Avatar = chatRoom.CreatedByUser.Avatar
                        } : null,
                        LastMessage = lastMessage != null ? new MessageDto
                        {
                            Id = lastMessage.Id,
                            Content = lastMessage.Content,
                            Type = lastMessage.Type,
                            CreatedAt = lastMessage.CreatedAt,
                            User = new UserDto
                            {
                                Id = lastMessage.User.Id,
                                Username = lastMessage.User.Username,
                                Avatar = lastMessage.User.Avatar
                            }
                        } : null,
                        UnreadMessageCount = unreadCount,
                        CurrentUserRole = userMembership?.Role,
                        IsCurrentUserMuted = userMembership?.IsMuted ?? false
                    });
                }

                var result = new ChatRoomPagedResult
                {
                    ChatRooms = chatRoomDtos,
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
                _logger.LogError(ex, "Error getting chat rooms");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get chat room by ID
        /// </summary>
        /// <param name="id">Chat room ID</param>
        /// <returns>Chat room details</returns>
        [HttpGet("{id}")]
        public async Task<ActionResult<ChatRoomDto>> GetChatRoom(Guid id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var chatRoom = await _context.ChatRooms
                    .Include(cr => cr.CreatedByUser)
                    .FirstOrDefaultAsync(cr => cr.Id == id);

                if (chatRoom == null)
                {
                    return NotFound(new { error = "Chat room not found" });
                }

                // Check access permissions
                var userMembership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && ucr.ChatRoomId == id && ucr.LeftAt == null);

                if (chatRoom.Type != ChatRoomType.Public && userMembership == null)
                {
                    return Forbid();
                }

                var memberCount = await _context.UserChatRooms
                    .CountAsync(ucr => ucr.ChatRoomId == id && ucr.LeftAt == null);

                var lastMessage = await _context.Messages
                    .Where(m => m.ChatRoomId == id)
                    .Include(m => m.User)
                    .OrderByDescending(m => m.CreatedAt)
                    .FirstOrDefaultAsync();

                var unreadCount = 0;
                if (userMembership != null)
                {
                    var lastReadTime = userMembership.LastReadMessageAt ?? userMembership.JoinedAt;
                    unreadCount = await _context.Messages
                        .CountAsync(m => m.ChatRoomId == id && m.CreatedAt > lastReadTime);
                }

                var chatRoomDto = new ChatRoomDto
                {
                    Id = chatRoom.Id,
                    Name = chatRoom.Name,
                    Description = chatRoom.Description,
                    Type = chatRoom.Type,
                    Avatar = chatRoom.Avatar,
                    IsActive = chatRoom.IsActive,
                    AllowFileUploads = chatRoom.AllowFileUploads,
                    MaxMembers = chatRoom.MaxMembers,
                    CurrentMemberCount = memberCount,
                    CreatedAt = chatRoom.CreatedAt,
                    CreatedByUser = chatRoom.CreatedByUser != null ? new UserDto
                    {
                        Id = chatRoom.CreatedByUser.Id,
                        Username = chatRoom.CreatedByUser.Username,
                        Avatar = chatRoom.CreatedByUser.Avatar
                    } : null,
                    LastMessage = lastMessage != null ? new MessageDto
                    {
                        Id = lastMessage.Id,
                        Content = lastMessage.Content,
                        Type = lastMessage.Type,
                        CreatedAt = lastMessage.CreatedAt,
                        User = new UserDto
                        {
                            Id = lastMessage.User.Id,
                            Username = lastMessage.User.Username,
                            Avatar = lastMessage.User.Avatar
                        }
                    } : null,
                    UnreadMessageCount = unreadCount,
                    CurrentUserRole = userMembership?.Role,
                    IsCurrentUserMuted = userMembership?.IsMuted ?? false
                };

                return Ok(chatRoomDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chat room {ChatRoomId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Create a new chat room
        /// </summary>
        /// <param name="createChatRoomDto">Chat room creation data</param>
        /// <returns>Created chat room</returns>
        [HttpPost]
        public async Task<ActionResult<ChatRoomDto>> CreateChatRoom([FromBody] CreateChatRoomDto createChatRoomDto)
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

                // Check if chat room name already exists
                var existingRoom = await _context.ChatRooms
                    .FirstOrDefaultAsync(cr => cr.Name.ToLower() == createChatRoomDto.Name.ToLower());

                if (existingRoom != null)
                {
                    return BadRequest(new { error = "Chat room name already exists" });
                }

                var chatRoom = new ChatRoom
                {
                    Name = createChatRoomDto.Name,
                    Description = createChatRoomDto.Description,
                    Type = createChatRoomDto.Type,
                    Avatar = createChatRoomDto.Avatar,
                    AllowFileUploads = createChatRoomDto.AllowFileUploads,
                    MaxMembers = createChatRoomDto.MaxMembers,
                    CreatedByUserId = currentUserId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.ChatRooms.Add(chatRoom);

                // Add creator as owner
                var creatorMembership = new UserChatRoom
                {
                    UserId = currentUserId.Value,
                    ChatRoomId = chatRoom.Id,
                    Role = UserRole.Owner,
                    JoinedAt = DateTime.UtcNow
                };

                _context.UserChatRooms.Add(creatorMembership);

                // Add initial members if provided
                if (createChatRoomDto.InitialMemberIds?.Any() == true)
                {
                    foreach (var memberId in createChatRoomDto.InitialMemberIds.Where(id => id != currentUserId))
                    {
                        var membership = new UserChatRoom
                        {
                            UserId = memberId,
                            ChatRoomId = chatRoom.Id,
                            Role = UserRole.Member,
                            JoinedAt = DateTime.UtcNow
                        };

                        _context.UserChatRooms.Add(membership);
                    }
                }

                await _context.SaveChangesAsync();

                var creator = await _context.Users.FindAsync(currentUserId);

                var chatRoomDto = new ChatRoomDto
                {
                    Id = chatRoom.Id,
                    Name = chatRoom.Name,
                    Description = chatRoom.Description,
                    Type = chatRoom.Type,
                    Avatar = chatRoom.Avatar,
                    IsActive = chatRoom.IsActive,
                    AllowFileUploads = chatRoom.AllowFileUploads,
                    MaxMembers = chatRoom.MaxMembers,
                    CurrentMemberCount = (createChatRoomDto.InitialMemberIds?.Count ?? 0) + 1,
                    CreatedAt = chatRoom.CreatedAt,
                    CreatedByUser = creator != null ? new UserDto
                    {
                        Id = creator.Id,
                        Username = creator.Username,
                        Avatar = creator.Avatar
                    } : null,
                    CurrentUserRole = UserRole.Owner,
                    IsCurrentUserMuted = false
                };

                _logger.LogInformation("Chat room created: {ChatRoomId} by {UserId}", chatRoom.Id, currentUserId);

                return CreatedAtAction(nameof(GetChatRoom), new { id = chatRoom.Id }, chatRoomDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating chat room");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Update chat room
        /// </summary>
        /// <param name="id">Chat room ID</param>
        /// <param name="updateChatRoomDto">Update data</param>
        /// <returns>Updated chat room</returns>
        [HttpPut("{id}")]
        public async Task<ActionResult<ChatRoomDto>> UpdateChatRoom(Guid id, [FromBody] UpdateChatRoomDto updateChatRoomDto)
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

                var chatRoom = await _context.ChatRooms.FindAsync(id);
                if (chatRoom == null)
                {
                    return NotFound(new { error = "Chat room not found" });
                }

                // Check permissions - only owners and admins can update
                var userMembership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && ucr.ChatRoomId == id && ucr.LeftAt == null);

                if (userMembership == null || (userMembership.Role != UserRole.Owner && userMembership.Role != UserRole.Admin))
                {
                    return Forbid();
                }

                // Update fields
                if (!string.IsNullOrEmpty(updateChatRoomDto.Name))
                {
                    // Check if new name already exists
                    var existingRoom = await _context.ChatRooms
                        .FirstOrDefaultAsync(cr => cr.Name.ToLower() == updateChatRoomDto.Name.ToLower() && cr.Id != id);

                    if (existingRoom != null)
                    {
                        return BadRequest(new { error = "Chat room name already exists" });
                    }

                    chatRoom.Name = updateChatRoomDto.Name;
                }

                if (updateChatRoomDto.Description != null)
                    chatRoom.Description = updateChatRoomDto.Description;

                if (!string.IsNullOrEmpty(updateChatRoomDto.Avatar))
                    chatRoom.Avatar = updateChatRoomDto.Avatar;

                if (updateChatRoomDto.AllowFileUploads.HasValue)
                    chatRoom.AllowFileUploads = updateChatRoomDto.AllowFileUploads.Value;

                if (updateChatRoomDto.MaxMembers.HasValue)
                    chatRoom.MaxMembers = updateChatRoomDto.MaxMembers.Value;

                chatRoom.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Chat room updated: {ChatRoomId} by {UserId}", id, currentUserId);

                // Return updated chat room
                return await GetChatRoom(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating chat room {ChatRoomId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Delete chat room
        /// </summary>
        /// <param name="id">Chat room ID</param>
        /// <returns>Deletion confirmation</returns>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteChatRoom(Guid id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var chatRoom = await _context.ChatRooms.FindAsync(id);
                if (chatRoom == null)
                {
                    return NotFound(new { error = "Chat room not found" });
                }

                // Check permissions - only owner can delete
                var userMembership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && ucr.ChatRoomId == id && ucr.LeftAt == null);

                if (userMembership == null || userMembership.Role != UserRole.Owner)
                {
                    return Forbid();
                }

                // Soft delete - just mark as inactive
                chatRoom.IsActive = false;
                chatRoom.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Chat room deleted: {ChatRoomId} by {UserId}", id, currentUserId);

                return Ok(new { message = "Chat room deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting chat room {ChatRoomId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get chat room members
        /// </summary>
        /// <param name="id">Chat room ID</param>
        /// <returns>List of chat room members</returns>
        [HttpGet("{id}/members")]
        public async Task<ActionResult<List<ChatRoomMemberDto>>> GetChatRoomMembers(Guid id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                // Check if user has access to the chat room
                var userMembership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && ucr.ChatRoomId == id && ucr.LeftAt == null);

                var chatRoom = await _context.ChatRooms.FindAsync(id);
                if (chatRoom == null)
                {
                    return NotFound(new { error = "Chat room not found" });
                }

                if (chatRoom.Type != ChatRoomType.Public && userMembership == null)
                {
                    return Forbid();
                }

                var members = await _context.UserChatRooms
                    .Where(ucr => ucr.ChatRoomId == id && ucr.LeftAt == null)
                    .Include(ucr => ucr.User)
                    .OrderBy(ucr => ucr.Role)
                    .ThenBy(ucr => ucr.User.Username)
                    .Select(ucr => new ChatRoomMemberDto
                    {
                        Id = ucr.Id,
                        UserId = ucr.UserId,
                        ChatRoomId = ucr.ChatRoomId,
                        Role = ucr.Role,
                        IsMuted = ucr.IsMuted,
                        IsBlocked = ucr.IsBlocked,
                        JoinedAt = ucr.JoinedAt,
                        LeftAt = ucr.LeftAt,
                        User = new UserDto
                        {
                            Id = ucr.User.Id,
                            Username = ucr.User.Username,
                            Email = ucr.User.Email,
                            Avatar = ucr.User.Avatar,
                            Bio = ucr.User.Bio,
                            IsOnline = ucr.User.IsOnline,
                            LastSeen = ucr.User.LastSeen,
                            CreatedAt = ucr.User.CreatedAt
                        }
                    })
                    .ToListAsync();

                return Ok(members);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chat room members for {ChatRoomId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Add member to chat room
        /// </summary>
        /// <param name="id">Chat room ID</param>
        /// <param name="addMemberDto">Member data</param>
        /// <returns>Added member data</returns>
        [HttpPost("{id}/members")]
        public async Task<ActionResult<ChatRoomMemberDto>> AddChatRoomMember(Guid id, [FromBody] AddChatRoomMemberDto addMemberDto)
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

                var chatRoom = await _context.ChatRooms.FindAsync(id);
                if (chatRoom == null)
                {
                    return NotFound(new { error = "Chat room not found" });
                }

                // Check permissions - only admins and owners can add members to private rooms
                var userMembership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && ucr.ChatRoomId == id && ucr.LeftAt == null);

                if (chatRoom.Type != ChatRoomType.Public)
                {
                    if (userMembership == null || 
                        (userMembership.Role != UserRole.Owner && 
                         userMembership.Role != UserRole.Admin && 
                         userMembership.Role != UserRole.Moderator))
                    {
                        return Forbid();
                    }
                }

                // Check if user to be added exists
                var userToAdd = await _context.Users.FindAsync(addMemberDto.UserId);
                if (userToAdd == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                // Check if user is already a member
                var existingMembership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == addMemberDto.UserId && ucr.ChatRoomId == id);

                if (existingMembership != null)
                {
                    if (existingMembership.LeftAt == null)
                    {
                        return BadRequest(new { error = "User is already a member of this chat room" });
                    }
                    else
                    {
                        // Rejoin - update existing membership
                        existingMembership.LeftAt = null;
                        existingMembership.Role = addMemberDto.Role;
                        existingMembership.JoinedAt = DateTime.UtcNow;
                    }
                }
                else
                {
                    // Check room capacity
                    var currentMemberCount = await _context.UserChatRooms
                        .CountAsync(ucr => ucr.ChatRoomId == id && ucr.LeftAt == null);

                    if (currentMemberCount >= chatRoom.MaxMembers)
                    {
                        return BadRequest(new { error = "Chat room has reached maximum capacity" });
                    }

                    // Create new membership
                    var newMembership = new UserChatRoom
                    {
                        UserId = addMemberDto.UserId,
                        ChatRoomId = id,
                        Role = addMemberDto.Role,
                        JoinedAt = DateTime.UtcNow
                    };

                    _context.UserChatRooms.Add(newMembership);
                    existingMembership = newMembership;
                }

                await _context.SaveChangesAsync();

                var memberDto = new ChatRoomMemberDto
                {
                    Id = existingMembership.Id,
                    UserId = existingMembership.UserId,
                    ChatRoomId = existingMembership.ChatRoomId,
                    Role = existingMembership.Role,
                    IsMuted = existingMembership.IsMuted,
                    IsBlocked = existingMembership.IsBlocked,
                    JoinedAt = existingMembership.JoinedAt,
                    LeftAt = existingMembership.LeftAt,
                    User = new UserDto
                    {
                        Id = userToAdd.Id,
                        Username = userToAdd.Username,
                        Email = userToAdd.Email,
                        Avatar = userToAdd.Avatar,
                        Bio = userToAdd.Bio,
                        IsOnline = userToAdd.IsOnline,
                        LastSeen = userToAdd.LastSeen,
                        CreatedAt = userToAdd.CreatedAt
                    }
                };

                _logger.LogInformation("User {UserId} added to chat room {ChatRoomId} by {CurrentUserId}",
                    addMemberDto.UserId, id, currentUserId);

                return CreatedAtAction(nameof(GetChatRoomMembers), new { id }, memberDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding member to chat room {ChatRoomId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Remove member from chat room
        /// </summary>
        /// <param name="id">Chat room ID</param>
        /// <param name="userId">User ID to remove</param>
        /// <returns>Removal confirmation</returns>
        [HttpDelete("{id}/members/{userId}")]
        public async Task<ActionResult> RemoveChatRoomMember(Guid id, Guid userId)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var chatRoom = await _context.ChatRooms.FindAsync(id);
                if (chatRoom == null)
                {
                    return NotFound(new { error = "Chat room not found" });
                }

                var membershipToRemove = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == userId && ucr.ChatRoomId == id && ucr.LeftAt == null);

                if (membershipToRemove == null)
                {
                    return NotFound(new { error = "User is not a member of this chat room" });
                }

                // Check permissions
                var currentUserMembership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && ucr.ChatRoomId == id && ucr.LeftAt == null);

                // Users can remove themselves, or admins/owners can remove others
                if (currentUserId != userId)
                {
                    if (currentUserMembership == null ||
                        (currentUserMembership.Role != UserRole.Owner &&
                         currentUserMembership.Role != UserRole.Admin &&
                         currentUserMembership.Role != UserRole.Moderator))
                    {
                        return Forbid();
                    }

                    // Can't remove users with equal or higher role
                    if (membershipToRemove.Role >= currentUserMembership.Role)
                    {
                        return Forbid();
                    }
                }

                // Mark as left
                membershipToRemove.LeftAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("User {UserId} removed from chat room {ChatRoomId} by {CurrentUserId}",
                    userId, id, currentUserId);

                return Ok(new { message = "Member removed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing member from chat room {ChatRoomId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Join a public chat room
        /// </summary>
        /// <param name="id">Chat room ID</param>
        /// <returns>Membership confirmation</returns>
        [HttpPost("{id}/join")]
        public async Task<ActionResult<ChatRoomMemberDto>> JoinChatRoom(Guid id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var chatRoom = await _context.ChatRooms.FindAsync(id);
                if (chatRoom == null)
                {
                    return NotFound(new { error = "Chat room not found" });
                }

                if (chatRoom.Type != ChatRoomType.Public)
                {
                    return BadRequest(new { error = "Cannot join private chat rooms directly" });
                }

                // Check if already a member
                var existingMembership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && ucr.ChatRoomId == id);

                if (existingMembership != null && existingMembership.LeftAt == null)
                {
                    return BadRequest(new { error = "Already a member of this chat room" });
                }

                // Check room capacity
                var currentMemberCount = await _context.UserChatRooms
                    .CountAsync(ucr => ucr.ChatRoomId == id && ucr.LeftAt == null);

                if (currentMemberCount >= chatRoom.MaxMembers)
                {
                    return BadRequest(new { error = "Chat room has reached maximum capacity" });
                }

                if (existingMembership != null)
                {
                    // Rejoin
                    existingMembership.LeftAt = null;
                    existingMembership.JoinedAt = DateTime.UtcNow;
                }
                else
                {
                    // Create new membership
                    existingMembership = new UserChatRoom
                    {
                        UserId = currentUserId.Value,
                        ChatRoomId = id,
                        Role = UserRole.Member,
                        JoinedAt = DateTime.UtcNow
                    };

                    _context.UserChatRooms.Add(existingMembership);
                }

                await _context.SaveChangesAsync();

                var user = await _context.Users.FindAsync(currentUserId);

                var memberDto = new ChatRoomMemberDto
                {
                    Id = existingMembership.Id,
                    UserId = existingMembership.UserId,
                    ChatRoomId = existingMembership.ChatRoomId,
                    Role = existingMembership.Role,
                    IsMuted = existingMembership.IsMuted,
                    IsBlocked = existingMembership.IsBlocked,
                    JoinedAt = existingMembership.JoinedAt,
                    LeftAt = existingMembership.LeftAt,
                    User = new UserDto
                    {
                        Id = user!.Id,
                        Username = user.Username,
                        Email = user.Email,
                        Avatar = user.Avatar,
                        Bio = user.Bio,
                        IsOnline = user.IsOnline,
                        LastSeen = user.LastSeen,
                        CreatedAt = user.CreatedAt
                    }
                };

                _logger.LogInformation("User {UserId} joined chat room {ChatRoomId}", currentUserId, id);

                return Ok(memberDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining chat room {ChatRoomId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Leave a chat room
        /// </summary>
        /// <param name="id">Chat room ID</param>
        /// <returns>Leave confirmation</returns>
        [HttpPost("{id}/leave")]
        public async Task<ActionResult> LeaveChatRoom(Guid id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                if (currentUserId == null)
                {
                    return Unauthorized();
                }

                var membership = await _context.UserChatRooms
                    .FirstOrDefaultAsync(ucr => ucr.UserId == currentUserId && ucr.ChatRoomId == id && ucr.LeftAt == null);

                if (membership == null)
                {
                    return NotFound(new { error = "Not a member of this chat room" });
                }

                // Mark as left
                membership.LeftAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("User {UserId} left chat room {ChatRoomId}", currentUserId, id);

                return Ok(new { message = "Left chat room successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving chat room {ChatRoomId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            return userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId) ? userId : null;
        }
    }
} 