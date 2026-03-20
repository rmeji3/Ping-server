using Ping.Models;
using Ping.Models.Notifications;

namespace Ping.Services.Notifications;

public interface INotificationService
{
    Task SendNotificationAsync(Notification notification);
    Task<Ping.Dtos.Common.PaginatedResult<Ping.Dtos.Notifications.NotificationDto>> GetNotificationsAsync(string userId, int pageNumber = 1, int pageSize = 20);
    Task<int> GetUnreadCountAsync(string userId);
    Task MarkAsReadAsync(string userId, string notificationId);
    Task MarkAllAsReadAsync(string userId);
    Task DeleteNotificationAsync(string userId, string notificationId);
    Task DeleteAllNotificationsAsync(string userId);
    
    // Device management
    Task RegisterDeviceAsync(string userId, string deviceToken, DevicePlatform platform);
    
    // Preferences
    Task<List<Ping.Dtos.Notifications.NotificationPreferenceDto>> GetPreferencesAsync(string userId);
    Task UpdatePreferenceAsync(string userId, NotificationType type, bool isEnabled);
}

