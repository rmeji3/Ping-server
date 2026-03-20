using System.ComponentModel.DataAnnotations;

namespace Ping.Models.Notifications;

public enum DevicePlatform
{
    Apple = 0,
    Android = 1
}

public class UserDevice
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string DeviceToken { get; set; } = string.Empty;

    [Required]
    public string EndpointArn { get; set; } = string.Empty;

    public DevicePlatform Platform { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
