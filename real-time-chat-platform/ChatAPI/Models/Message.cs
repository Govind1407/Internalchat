using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChatAPI.Models
{
    public class Message
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }

        [Required]
        public Guid ChatRoomId { get; set; }

        [Required]
        [StringLength(2000)]
        public string Content { get; set; } = string.Empty;

        public MessageType Type { get; set; } = MessageType.Text;

        [StringLength(500)]
        public string? AttachmentUrl { get; set; }

        [StringLength(100)]
        public string? AttachmentName { get; set; }

        public long? AttachmentSize { get; set; }

        public bool IsEdited { get; set; } = false;

        public DateTime? EditedAt { get; set; }

        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Reply functionality
        public Guid? ReplyToMessageId { get; set; }

        // Navigation properties
        public virtual User User { get; set; } = null!;
        public virtual ChatRoom ChatRoom { get; set; } = null!;
        public virtual Message? ReplyToMessage { get; set; }
        public virtual ICollection<Message> Replies { get; set; } = new List<Message>();
        public virtual ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();
        public virtual ICollection<MessageMention> Mentions { get; set; } = new List<MessageMention>();
    }

    public class MessageReaction
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid MessageId { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [Required]
        [StringLength(10)]
        public string Emoji { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual Message Message { get; set; } = null!;
        public virtual User User { get; set; } = null!;
    }

    public class MessageMention
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid MessageId { get; set; }

        [Required]
        public Guid MentionedUserId { get; set; }

        public int StartIndex { get; set; }

        public int Length { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual Message Message { get; set; } = null!;
        public virtual User MentionedUser { get; set; } = null!;
    }

    public enum MessageType
    {
        Text = 0,
        Image = 1,
        File = 2,
        Audio = 3,
        Video = 4,
        System = 5
    }

    public class MessageDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid ChatRoomId { get; set; }
        public string Content { get; set; } = string.Empty;
        public MessageType Type { get; set; }
        public string? AttachmentUrl { get; set; }
        public string? AttachmentName { get; set; }
        public long? AttachmentSize { get; set; }
        public bool IsEdited { get; set; }
        public DateTime? EditedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? ReplyToMessageId { get; set; }
        
        // Related data
        public UserDto User { get; set; } = null!;
        public MessageDto? ReplyToMessage { get; set; }
        public List<MessageReactionDto> Reactions { get; set; } = new();
        public List<MessageMentionDto> Mentions { get; set; } = new();
    }

    public class CreateMessageDto
    {
        [Required]
        [StringLength(2000, MinimumLength = 1)]
        public string Content { get; set; } = string.Empty;

        public MessageType Type { get; set; } = MessageType.Text;

        public string? AttachmentUrl { get; set; }
        public string? AttachmentName { get; set; }
        public long? AttachmentSize { get; set; }

        public Guid? ReplyToMessageId { get; set; }
    }

    public class UpdateMessageDto
    {
        [Required]
        [StringLength(2000, MinimumLength = 1)]
        public string Content { get; set; } = string.Empty;
    }

    public class MessageReactionDto
    {
        public Guid Id { get; set; }
        public Guid MessageId { get; set; }
        public Guid UserId { get; set; }
        public string Emoji { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public UserDto User { get; set; } = null!;
    }

    public class CreateMessageReactionDto
    {
        [Required]
        [StringLength(10)]
        public string Emoji { get; set; } = string.Empty;
    }

    public class MessageMentionDto
    {
        public Guid Id { get; set; }
        public Guid MessageId { get; set; }
        public Guid MentionedUserId { get; set; }
        public int StartIndex { get; set; }
        public int Length { get; set; }
        public UserDto MentionedUser { get; set; } = null!;
    }

    public class MessageSearchDto
    {
        public string? Query { get; set; }
        public Guid? ChatRoomId { get; set; }
        public Guid? UserId { get; set; }
        public MessageType? Type { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class MessagePagedResult
    {
        public List<MessageDto> Messages { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasNextPage { get; set; }
        public bool HasPreviousPage { get; set; }
    }
} 