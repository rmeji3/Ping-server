using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Conquest.Models;

public enum NotificationType
{
    FriendRequest = 0,
    ReviewLike = 1,
    System = 2,
    EventInvite = 3
}

public class Notification
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // receiver
    [Required]
    public string UserId { get; set; } = string.Empty;

    // sender (optional, e.g. system messages don't have a sender user)
    public string? SenderId { get; set; }
    public string? SenderName { get; set; }
    public string? SenderProfileImageUrl { get; set; }

    [Required]
    public NotificationType Type { get; set; }

    [Required]
    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    // e.g. ReviewId, PlaceId, EventId depending on context
    public string? ReferenceId { get; set; }
    
    // extra metadata if needed (JSON)
    public string? Metadata { get; set; }

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
