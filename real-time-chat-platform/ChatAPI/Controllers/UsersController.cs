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
    public class UsersController : ControllerBase
    {
        private readonly ChatDbContext _context;
        private readonly ILogger<UsersController> _logger;

        public UsersController(ChatDbContext context, ILogger<UsersController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get all users with pagination and search
        /// </summary>
        /// <param name="page">Page number (default: 1)</param>
        /// <param name="pageSize">Page size (default: 20, max: 100)</param>
        /// <param name="search">Search term for username</param>
        /// <param name="onlineOnly">Filter for online users only</param>
        /// <returns>Paginated list of users</returns>
        [HttpGet]
        public async Task<ActionResult<object>> GetUsers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] bool onlineOnly = false)
        {
            try
            {
                // Validate pagination parameters
                page = Math.Max(1, page);
                pageSize = Math.Min(100, Math.Max(1, pageSize));

                var query = _context.Users.AsQueryable();

                // Apply search filter
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(u => u.Username.Contains(search) || u.Email.Contains(search));
                }

                // Apply online filter
                if (onlineOnly)
                {
                    query = query.Where(u => u.IsOnline);
                }

                // Order by username
                query = query.OrderBy(u => u.Username);

                var totalCount = await query.CountAsync();
                var users = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(u => new UserDto
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Email = u.Email,
                        Avatar = u.Avatar,
                        Bio = u.Bio,
                        IsOnline = u.IsOnline,
                        LastSeen = u.LastSeen,
                        CreatedAt = u.CreatedAt
                    })
                    .ToListAsync();

                var result = new
                {
                    users,
                    pagination = new
                    {
                        currentPage = page,
                        pageSize,
                        totalCount,
                        totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                        hasNextPage = page * pageSize < totalCount,
                        hasPreviousPage = page > 1
                    }
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get user by ID
        /// </summary>
        /// <param name="id">User ID</param>
        /// <returns>User data</returns>
        [HttpGet("{id}")]
        public async Task<ActionResult<UserDto>> GetUser(Guid id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);

                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                var userDto = new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Avatar = user.Avatar,
                    Bio = user.Bio,
                    IsOnline = user.IsOnline,
                    LastSeen = user.LastSeen,
                    CreatedAt = user.CreatedAt
                };

                return Ok(userDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user {UserId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get online users
        /// </summary>
        /// <returns>List of currently online users</returns>
        [HttpGet("online")]
        public async Task<ActionResult<List<UserDto>>> GetOnlineUsers()
        {
            try
            {
                var onlineUsers = await _context.Users
                    .Where(u => u.IsOnline)
                    .OrderBy(u => u.Username)
                    .Select(u => new UserDto
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Email = u.Email,
                        Avatar = u.Avatar,
                        Bio = u.Bio,
                        IsOnline = u.IsOnline,
                        LastSeen = u.LastSeen,
                        CreatedAt = u.CreatedAt
                    })
                    .ToListAsync();

                return Ok(onlineUsers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting online users");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get user's chat rooms
        /// </summary>
        /// <param name="id">User ID</param>
        /// <returns>List of chat rooms the user is a member of</returns>
        [HttpGet("{id}/chatrooms")]
        public async Task<ActionResult<List<ChatRoomDto>>> GetUserChatRooms(Guid id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                
                // Users can only view their own chat rooms or if they're admin
                if (currentUserId != id && !await IsCurrentUserAdmin())
                {
                    return Forbid();
                }

                var chatRooms = await _context.UserChatRooms
                    .Where(ucr => ucr.UserId == id && ucr.LeftAt == null)
                    .Include(ucr => ucr.ChatRoom)
                        .ThenInclude(cr => cr.CreatedByUser)
                    .Select(ucr => new ChatRoomDto
                    {
                        Id = ucr.ChatRoom.Id,
                        Name = ucr.ChatRoom.Name,
                        Description = ucr.ChatRoom.Description,
                        Type = ucr.ChatRoom.Type,
                        Avatar = ucr.ChatRoom.Avatar,
                        IsActive = ucr.ChatRoom.IsActive,
                        AllowFileUploads = ucr.ChatRoom.AllowFileUploads,
                        MaxMembers = ucr.ChatRoom.MaxMembers,
                        CreatedAt = ucr.ChatRoom.CreatedAt,
                        CreatedByUser = ucr.ChatRoom.CreatedByUser != null ? new UserDto
                        {
                            Id = ucr.ChatRoom.CreatedByUser.Id,
                            Username = ucr.ChatRoom.CreatedByUser.Username,
                            Avatar = ucr.ChatRoom.CreatedByUser.Avatar
                        } : null,
                        CurrentUserRole = ucr.Role,
                        IsCurrentUserMuted = ucr.IsMuted,
                        CurrentMemberCount = ucr.ChatRoom.UserChatRooms.Count(x => x.LeftAt == null)
                    })
                    .ToListAsync();

                return Ok(chatRooms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user chat rooms for user {UserId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Search users by username or email
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="limit">Maximum number of results (default: 10)</param>
        /// <returns>List of matching users</returns>
        [HttpGet("search")]
        public async Task<ActionResult<List<UserDto>>> SearchUsers(
            [FromQuery] string query,
            [FromQuery] int limit = 10)
        {
            try
            {
                if (string.IsNullOrEmpty(query) || query.Length < 2)
                {
                    return BadRequest(new { error = "Search query must be at least 2 characters long" });
                }

                limit = Math.Min(50, Math.Max(1, limit));

                var users = await _context.Users
                    .Where(u => u.Username.Contains(query) || u.Email.Contains(query))
                    .OrderBy(u => u.Username)
                    .Take(limit)
                    .Select(u => new UserDto
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Email = u.Email,
                        Avatar = u.Avatar,
                        Bio = u.Bio,
                        IsOnline = u.IsOnline,
                        LastSeen = u.LastSeen,
                        CreatedAt = u.CreatedAt
                    })
                    .ToListAsync();

                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching users with query: {Query}", query);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get user statistics
        /// </summary>
        /// <param name="id">User ID</param>
        /// <returns>User statistics</returns>
        [HttpGet("{id}/stats")]
        public async Task<ActionResult<object>> GetUserStats(Guid id)
        {
            try
            {
                var currentUserId = GetCurrentUserId();
                
                // Users can only view their own stats or if they're admin
                if (currentUserId != id && !await IsCurrentUserAdmin())
                {
                    return Forbid();
                }

                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                var stats = new
                {
                    totalMessages = await _context.Messages.CountAsync(m => m.UserId == id),
                    totalChatRooms = await _context.UserChatRooms.CountAsync(ucr => ucr.UserId == id && ucr.LeftAt == null),
                    totalReactions = await _context.MessageReactions.CountAsync(mr => mr.UserId == id),
                    mentionsReceived = await _context.MessageMentions.CountAsync(mm => mm.MentionedUserId == id),
                    firstMessageDate = await _context.Messages
                        .Where(m => m.UserId == id)
                        .OrderBy(m => m.CreatedAt)
                        .Select(m => (DateTime?)m.CreatedAt)
                        .FirstOrDefaultAsync(),
                    lastMessageDate = await _context.Messages
                        .Where(m => m.UserId == id)
                        .OrderByDescending(m => m.CreatedAt)
                        .Select(m => (DateTime?)m.CreatedAt)
                        .FirstOrDefaultAsync(),
                    joinDate = user.CreatedAt,
                    lastSeen = user.LastSeen,
                    isOnline = user.IsOnline
                };

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user stats for user {UserId}", id);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            return userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId) ? userId : null;
        }

        private async Task<bool> IsCurrentUserAdmin()
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null) return false;

            // Check if user has admin role in any chat room
            // For simplicity, we'll consider users who created chat rooms as admins
            return await _context.ChatRooms.AnyAsync(cr => cr.CreatedByUserId == currentUserId);
        }
    }
} 