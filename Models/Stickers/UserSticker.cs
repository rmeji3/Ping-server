using System.ComponentModel.DataAnnotations;

namespace Ping.Models.Stickers;

/// <summary>
/// Ownership record: which stickers a user owns and can place on their profile.
/// Unique per (UserId, StickerId).
/// </summary>
public class UserSticker
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string StickerId { get; set; } = string.Empty;

    public Sticker? Sticker { get; set; }

    public DateTimeOffset AcquiredUtc { get; set; } = DateTimeOffset.UtcNow;
}
