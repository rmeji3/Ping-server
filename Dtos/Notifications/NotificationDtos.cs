using System.ComponentModel.DataAnnotations;
using Ping.Models.Notifications;

namespace Ping.Dtos.Notifications;

public record RegisterDeviceDto(
    [Required] string DeviceToken,
    [Required] DevicePlatform Platform
);

public record NotificationPreferenceDto(
    NotificationType Type,
    string TypeName,
    bool IsEnabled
);

public record UpdateNotificationPreferenceDto(
    [Required] NotificationType Type,
    [Required] bool IsEnabled
);

public record NotificationUserDto(
    string Id,
    string UserName,
    string? ProfileImageUrl
);

public record NotificationDto(
    string Id,
    NotificationUserDto? User,
    NotificationType Type,
    string Title,
    string Content,
    string? RelatedEntityId,
    string? ImageThumbnailUrl,
    bool IsRead,
    DateTime CreatedAt,
    string? Metadata = null
);

public record UnreadCountDto(int Count);
