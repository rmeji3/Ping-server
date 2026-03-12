using System.ComponentModel.DataAnnotations;

namespace Ping.Models.Events;

public class EventComment
{
    public int Id { get; set; }

    [MaxLength(500)]
    public required string Content { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // FK to Event
    public int EventId { get; set; }
    public Event? Event { get; set; }

    // User reference (no navigation — users live in AuthDbContext)
    public required string UserId { get; set; }

    // Reaction aggregates
    public int LikeCount { get; set; }
    public int DislikeCount { get; set; }

    // Reply support
    public int? ParentCommentId { get; set; }
    public EventComment? ParentComment { get; set; }
    public int ReplyCount { get; set; }

    // Reactions navigation
    public List<EventCommentReaction> Reactions { get; set; } = new();
}
