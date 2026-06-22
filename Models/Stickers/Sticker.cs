using System.ComponentModel.DataAnnotations;

namespace Ping.Models.Stickers;

/// <summary>
/// A sticker available in the catalog. Users acquire stickers (<see cref="UserSticker"/>)
/// and can place owned stickers on their profile header (<see cref="ProfileStickerPlacement"/>).
/// </summary>
public class Sticker
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Stable, unique identifier (e.g. "verified_badge"). Used for client-side rendering and seeding.</summary>
    [Required]
    [MaxLength(64)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional remote image for image stickers. Badge-style stickers are rendered client-side from <see cref="Key"/>.</summary>
    [MaxLength(2048)]
    public string? ImageUrl { get; set; }

    [MaxLength(32)]
    public string? Category { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Whether this sticker is part of the current marketplace rotation that users
    /// can freely claim. Admin-managed; a single active set swapped manually.</summary>
    public bool InRotation { get; set; } = false;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
