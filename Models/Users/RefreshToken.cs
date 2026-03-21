using System.ComponentModel.DataAnnotations;
using Ping.Models.AppUsers;

namespace Ping.Models.Users;

/// <summary>
/// Stores hashed refresh tokens tied to a user and device.
/// One user can have multiple active tokens (one per device).
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    [Required]
    public required string UserId { get; set; }

    // Store a SHA-256 hash of the token, never the raw value
    [Required, MaxLength(128)]
    public required string TokenHash { get; set; }

    // Identifies the device/client so we can revoke per-device
    [MaxLength(256)]
    public string? DeviceId { get; set; }

    public DateTime ExpiresUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    // Null until used; set when the token is exchanged for a new pair
    public DateTime? RevokedUtc { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresUtc;
    public bool IsRevoked => RevokedUtc is not null;
    public bool IsActive => !IsRevoked && !IsExpired;

    // Navigation
    public AppUser User { get; set; } = null!;
}
