namespace Ping.Models.Events;

public class EventCommentReaction
{
    public int Id { get; set; }

    public int CommentId { get; set; }
    public EventComment Comment { get; set; } = null!;

    public required string UserId { get; set; }

    /// <summary>
    /// +1 = like, -1 = dislike
    /// </summary>
    public int Value { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
