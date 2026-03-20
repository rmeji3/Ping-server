using Ping.Data.App;
using Ping.Models;
using Ping.Models.Notifications;
using Ping.Services.Redis;
using Microsoft.EntityFrameworkCore;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System.Text.Json;
using Ping.Dtos.Notifications;
using Ping.Dtos.Common;

namespace Ping.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _context;
    private readonly IRedisService _redis;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationService> _logger;
    private readonly IAmazonSimpleNotificationService _snsClient;

    public NotificationService(
        AppDbContext context,
        IRedisService redis,
        IConfiguration config,
        ILogger<NotificationService> logger,
        IAmazonSimpleNotificationService snsClient)
    {
        _context = context;
        _redis = redis;
        _config = config;
        _logger = logger;
        _snsClient = snsClient;
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

        // Check user preference for this notification type
        var preference = await _context.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == notification.UserId && p.Type == notification.Type);

        if (preference != null && !preference.IsEnabled)
        {
            _logger.LogInformation("Push notification disabled by user preference for User {UserId} Type {Type}", notification.UserId, notification.Type);
            return;
        }

        // Get user devices
        var devices = await _context.UserDevices
            .Where(d => d.UserId == notification.UserId)
            .ToListAsync();

        if (devices.Count == 0) return;

        var unreadCount = await GetUnreadCountAsync(notification.UserId);

        foreach (var device in devices)
        {
            try
            {
                await SendPushNotificationAsync(device, notification, unreadCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send push notification to device {DeviceId} for user {UserId}", device.Id, device.UserId);
                // If endpoint is disabled/invalid, we could remove it here
                if (ex.Message.Contains("EndpointDisabled") || ex.Message.Contains("NotFound"))
                {
                    _context.UserDevices.Remove(device);
                    await _context.SaveChangesAsync();
                }
            }
        }
    }

    private async Task SendPushNotificationAsync(UserDevice device, Notification notification, int badgeCount)
    {
        string messagePayload;

        if (device.Platform == DevicePlatform.Apple)
        {
            var aps = new
            {
                aps = new
                {
                    alert = new
                    {
                        title = notification.Title,
                        body = notification.Message
                    },
                    sound = "default",
                    badge = badgeCount,
                    category = notification.Type.ToString()
                },
                notificationId = notification.Id,
                referenceId = notification.ReferenceId,
                imageThumbnailUrl = notification.ImageThumbnailUrl,
                type = notification.Type.ToString()
            };

            var payload = new Dictionary<string, string>
            {
                { "default", notification.Message },
                { "APNS_SANDBOX", JsonSerializer.Serialize(aps) },
                { "APNS", JsonSerializer.Serialize(aps) }
            };

            messagePayload = JsonSerializer.Serialize(payload);
        }
        else
        {
            // Android/FCM (if needed later)
            return;
        }

        await _snsClient.PublishAsync(new PublishRequest
        {
            TargetArn = device.EndpointArn,
            Message = messagePayload,
            MessageStructure = "json"
        });
    }

    public async Task RegisterDeviceAsync(string userId, string deviceToken, DevicePlatform platform)
    {
        var platformArn = platform == DevicePlatform.Apple
            ? _config.GetValue<string>("AWS:SNS:ApnsSandboxArn")
            : _config.GetValue<string>("AWS:SNS:FcmArn");

        if (string.IsNullOrEmpty(platformArn))
        {
            _logger.LogError("SNS Platform Application ARN not configured for {Platform}", platform);
            throw new InvalidOperationException("Push notifications not configured.");
        }

        // Check if device already registered
        var existingDevice = await _context.UserDevices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceToken == deviceToken);

        if (existingDevice != null)
        {
            // Ensure endpoint is enabled in SNS
            try
            {
                await _snsClient.SetEndpointAttributesAsync(new SetEndpointAttributesRequest
                {
                    EndpointArn = existingDevice.EndpointArn,
                    Attributes = new Dictionary<string, string> { { "Enabled", "true" }, { "Token", deviceToken } }
                });
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update existing SNS endpoint {EndpointArn}. Re-creating.", existingDevice.EndpointArn);
                _context.UserDevices.Remove(existingDevice);
            }
        }

        // Create new endpoint
        var response = await _snsClient.CreatePlatformEndpointAsync(new CreatePlatformEndpointRequest
        {
            PlatformApplicationArn = platformArn,
            Token = deviceToken
        });

        var newDevice = new UserDevice
        {
            UserId = userId,
            DeviceToken = deviceToken,
            EndpointArn = response.EndpointArn,
            Platform = platform
        };

        _context.UserDevices.Add(newDevice);
        await _context.SaveChangesAsync();
    }

    public async Task<List<NotificationPreferenceDto>> GetPreferencesAsync(string userId)
    {
        var prefs = await _context.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync();
            
        // If empty, return defaults (all enabled) 
        // Or we could seed them. For now, returning existing ones.
        return prefs.Select(p => new NotificationPreferenceDto(p.Type, p.Type.ToString(), p.IsEnabled)).ToList();
    }

    public async Task UpdatePreferenceAsync(string userId, NotificationType type, bool isEnabled)
    {
        var pref = await _context.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Type == type);

        if (pref != null)
        {
            pref.IsEnabled = isEnabled;
        }
        else
        {
            _context.NotificationPreferences.Add(new NotificationPreference
            {
                UserId = userId,
                Type = type,
                IsEnabled = isEnabled
            });
        }

        await _context.SaveChangesAsync();
    }

    public async Task<PaginatedResult<NotificationDto>> GetNotificationsAsync(string userId, int pageNumber = 1, int pageSize = 20)
    {
        var query = _context.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(n => new NotificationDto(
            Id: n.Id,
            User: n.SenderId != null ? new NotificationUserDto(n.SenderId, n.SenderName ?? "Ping User", n.SenderProfileImageUrl) : null,
            Type: n.Type,
            Title: n.Title,
            Content: n.Message, 
            RelatedEntityId: n.ReferenceId,
            ImageThumbnailUrl: n.ImageThumbnailUrl,
            IsRead: n.IsRead,
            CreatedAt: n.CreatedAt,
            Metadata: n.Metadata
        )).ToList();

        // Let's re-verify DTO Content vs Message.
        // Frontend Notification interface has 'content: string'.
        // NotificationDto in C# should have 'Content'.
        // My previous replace_file_content for NotificationDtos.cs added 'string Content'.
        // So I map n.Message -> Content.

        return new PaginatedResult<NotificationDto>(dtos, totalCount, pageNumber, pageSize);
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

    public async Task DeleteNotificationAsync(string userId, string notificationId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.UserId == userId && n.Id == notificationId);

        if (notification != null)
        {
            _context.Notifications.Remove(notification);
            await _context.SaveChangesAsync();
        }
    }

    public async Task DeleteAllNotificationsAsync(string userId)
    {
        var notifications = await _context.Notifications
            .Where(n => n.UserId == userId)
            .ToListAsync();

        if (notifications.Any())
        {
            _context.Notifications.RemoveRange(notifications);
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
        else if (notification.Type == NotificationType.CommentReply)
        {
            limit = _config.GetValue<int>("NotificationRateLimits:CommentReplyLimitPerHour", 10);
            window = TimeSpan.FromHours(1);
        }
        else if (notification.Type == NotificationType.CommentLike || notification.Type == NotificationType.CommentDislike)
        {
            limit = _config.GetValue<int>("NotificationRateLimits:CommentReactionLimitPerHour", 5);
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

