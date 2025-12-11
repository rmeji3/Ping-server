using Conquest.Models;

namespace Conquest.Services.Notifications;

public interface INotificationService
{
    Task SendNotificationAsync(Notification notification);
    Task<List<Notification>> GetNotificationsAsync(string userId, int pageNumber = 1, int pageSize = 20);
    Task<int> GetUnreadCountAsync(string userId);
    Task MarkAsReadAsync(string userId, string notificationId);
    Task MarkAllAsReadAsync(string userId);
}
