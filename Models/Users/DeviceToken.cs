using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ping.Models.AppUsers;

namespace Ping.Models.Users;

public class DeviceToken
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    [Required]
    public string Token { get; set; } = null!;

    // "ios" or "android"
    [Required]
    public string Platform { get; set; } = "ios";

    // AWS SNS Endpoint ARN for this specific device
    public string? SnsEndpointArn { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
}
