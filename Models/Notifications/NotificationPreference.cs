using System.ComponentModel.DataAnnotations;

namespace Ping.Models.Notifications;

public class NotificationPreference
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public NotificationType Type { get; set; }

    public bool IsEnabled { get; set; } = true;
}
