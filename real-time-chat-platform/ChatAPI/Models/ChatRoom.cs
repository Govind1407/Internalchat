using System.ComponentModel.DataAnnotations;

namespace ChatAPI.Models
{
    public class ChatRoom
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public ChatRoomType Type { get; set; } = ChatRoomType.Public;

        [StringLength(500)]
        public string? Avatar { get; set; }

        public bool IsActive { get; set; } = true;

        public bool AllowFileUploads { get; set; } = true;

        public int MaxMembers { get; set; } = 1000;

        public Guid? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual User? CreatedByUser { get; set; }
        public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
        public virtual ICollection<UserChatRoom> UserChatRooms { get; set; } = new List<UserChatRoom>();
    }

    public class UserChatRoom
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }

        [Required]
        public Guid ChatRoomId { get; set; }

        public UserRole Role { get; set; } = UserRole.Member;

        public bool IsMuted { get; set; } = false;

        public bool IsBlocked { get; set; } = false;

        public DateTime? LastReadMessageAt { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LeftAt { get; set; }

        // Navigation properties
        public virtual User User { get; set; } = null!;
        public virtual ChatRoom ChatRoom { get; set; } = null!;
    }

    public enum ChatRoomType
    {
        Public = 0,
        Private = 1,
        DirectMessage = 2,
        Group = 3
    }

    public enum UserRole
    {
        Member = 0,
        Moderator = 1,
        Admin = 2,
        Owner = 3
    }

    public class ChatRoomDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public ChatRoomType Type { get; set; }
        public string? Avatar { get; set; }
        public bool IsActive { get; set; }
        public bool AllowFileUploads { get; set; }
        public int MaxMembers { get; set; }
        public int CurrentMemberCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public UserDto? CreatedByUser { get; set; }
        public MessageDto? LastMessage { get; set; }
        public int UnreadMessageCount { get; set; }
        public UserRole? CurrentUserRole { get; set; }
        public bool IsCurrentUserMuted { get; set; }
    }

    public class CreateChatRoomDto
    {
        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public ChatRoomType Type { get; set; } = ChatRoomType.Public;

        public string? Avatar { get; set; }

        public bool AllowFileUploads { get; set; } = true;

        public int MaxMembers { get; set; } = 1000;

        public List<Guid>? InitialMemberIds { get; set; }
    }

    public class UpdateChatRoomDto
    {
        [StringLength(100, MinimumLength = 1)]
        public string? Name { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public string? Avatar { get; set; }

        public bool? AllowFileUploads { get; set; }

        public int? MaxMembers { get; set; }
    }

    public class ChatRoomMemberDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid ChatRoomId { get; set; }
        public UserRole Role { get; set; }
        public bool IsMuted { get; set; }
        public bool IsBlocked { get; set; }
        public DateTime JoinedAt { get; set; }
        public DateTime? LeftAt { get; set; }
        public UserDto User { get; set; } = null!;
    }

    public class AddChatRoomMemberDto
    {
        [Required]
        public Guid UserId { get; set; }

        public UserRole Role { get; set; } = UserRole.Member;
    }

    public class UpdateChatRoomMemberDto
    {
        public UserRole? Role { get; set; }
        public bool? IsMuted { get; set; }
        public bool? IsBlocked { get; set; }
    }

    public class ChatRoomSearchDto
    {
        public string? Query { get; set; }
        public ChatRoomType? Type { get; set; }
        public bool? IsActive { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class ChatRoomPagedResult
    {
        public List<ChatRoomDto> ChatRooms { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasNextPage { get; set; }
        public bool HasPreviousPage { get; set; }
    }

    public class TypingIndicatorDto
    {
        public Guid ChatRoomId { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public bool IsTyping { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
} 