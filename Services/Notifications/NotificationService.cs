using Conquest.Data.App;
using Conquest.Models;
using Conquest.Services.Redis;
using Microsoft.EntityFrameworkCore;

namespace Conquest.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _context;
    private readonly IRedisService _redis;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        AppDbContext context,
        IRedisService redis,
        IConfiguration config,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _redis = redis;
        _config = config;
        _logger = logger;
    }

    public async Task SendNotificationAsync(Notification notification)
    {
        // Rate Limiting Logic
        if (await IsRateLimitedAsync(notification))
        {
            _logger.LogInformation("Notification rate limit exceeded for User {UserId} Type {Type}", notification.UserId, notification.Type);
            return;
        }

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();
    }

    public async Task<List<Notification>> GetNotificationsAsync(string userId, int pageNumber = 1, int pageSize = 20)
    {
        return await _context.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await _context.Notifications
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task MarkAsReadAsync(string userId, string notificationId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.UserId == userId && n.Id == notificationId);

        if (notification != null && !notification.IsRead)
        {
            notification.IsRead = true;
            await _context.SaveChangesAsync();
        }
    }

    public async Task MarkAllAsReadAsync(string userId)
    {
        var unreadNotifications = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        if (unreadNotifications.Any())
        {
            foreach (var n in unreadNotifications)
            {
                n.IsRead = true;
            }
            await _context.SaveChangesAsync();
        }
    }

    private async Task<bool> IsRateLimitedAsync(Notification notification)
    {
        // Only rate limit specific types
        if (notification.Type == NotificationType.System || notification.Type == NotificationType.EventInvite)
        {
            return false;
        }

        // Key: notify:limit:{Type}:{SenderId}:{ReceiverId}
        // If system message has no sender, we skip rate limiting or key by type only? 
        // Logic above handles System type. For FriendRequest/ReviewLike, SenderId should be present.
        if (string.IsNullOrEmpty(notification.SenderId))
        {
            return false;
        }

        string key = $"notify:limit:{notification.Type}:{notification.SenderId}:{notification.UserId}";
        
        int limit = 0;
        TimeSpan window = TimeSpan.Zero;

        if (notification.Type == NotificationType.FriendRequest)
        {
            limit = _config.GetValue<int>("NotificationRateLimits:FriendRequestLimitPer12Hours", 1);
            window = TimeSpan.FromHours(12);
        }
        else if (notification.Type == NotificationType.ReviewLike)
        {
            limit = _config.GetValue<int>("NotificationRateLimits:ReviewLikeLimitPerHour", 3);
            window = TimeSpan.FromHours(1);
        }
        else
        {
            // Default no limit for other types if they were added
            return false;
        }

        // Increment redis counter
        long count = await _redis.IncrementAsync(key, window);
        
        return count > limit;
    }
}
