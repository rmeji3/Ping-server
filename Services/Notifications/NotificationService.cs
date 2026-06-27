using Ping.Data.App;
using Ping.Models;
using Ping.Models.Notifications;
using Ping.Services.Redis;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using Ping.Dtos.Notifications;
using Ping.Dtos.Common;

namespace Ping.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _context;
    private readonly IRedisService _redis;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationService> _logger;
    private readonly HttpClient _httpClient;

    public NotificationService(
        AppDbContext context,
        IRedisService redis,
        IConfiguration config,
        ILogger<NotificationService> logger,
        HttpClient httpClient)
    {
        _context = context;
        _redis = redis;
        _config = config;
        _logger = logger;
        _httpClient = httpClient;
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
            catch (Exception ex) when (ex.Message.Contains("DeviceNotRegistered"))
            {
                _logger.LogWarning("Removing unregistered push device {DeviceId} for user {UserId}", device.Id, device.UserId);
                _context.UserDevices.Remove(device);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send push notification to device {DeviceId} for user {UserId}", device.Id, device.UserId);
            }
        }
    }

    private async Task SendPushNotificationAsync(UserDevice device, Notification notification, int badgeCount)
    {
        var payload = new
        {
            to = device.DeviceToken,
            title = notification.Title,
            body = notification.Message,
            sound = "default",
            badge = badgeCount,
            data = new
            {
                notificationId = notification.Id,
                referenceId = notification.ReferenceId,
                imageThumbnailUrl = notification.ImageThumbnailUrl,
                type = notification.Type.ToString()
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://exp.host/--/api/v2/push/send");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

        var token = _config.GetValue<string>("Expo:AccessToken");
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Expo push HTTP request failed: {Response}", responseContent);
            if (responseContent.Contains("DeviceNotRegistered"))
            {
                throw new Exception("DeviceNotRegistered");
            }
            throw new InvalidOperationException("Expo push failed: " + responseContent);
        }

        // Check Expo specific error in the response body
        // Response format: { "data": { "status": "ok" | "error", "message": "...", "details": { "error": "DeviceNotRegistered" } } }
        if (responseContent.Contains("\"status\":\"error\"") || responseContent.Contains("\"status\": \"error\""))
        {
            _logger.LogError("Expo push API returned error: {Response}", responseContent);
            
            if (responseContent.Contains("DeviceNotRegistered"))
            {
                throw new Exception("DeviceNotRegistered");
            }
        }
    }

    public async Task RegisterDeviceAsync(string userId, string deviceToken, DevicePlatform platform, bool isProduction)
    {
        // Upsert keyed on (userId, deviceToken). A tracked find-then-update keeps this
        // provider-agnostic (device registration is infrequent, so the extra read is
        // negligible). The unique-violation catch still handles a concurrent insert.
        var existing = await _context.UserDevices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceToken == deviceToken);

        if (existing != null)
        {
            existing.Platform = platform;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Updated push device for User {UserId} Platform {Platform}", userId, platform);
            return;
        }

        try
        {
            _context.UserDevices.Add(new UserDevice
            {
                UserId = userId,
                DeviceToken = deviceToken,
                Platform = platform
            });
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Lost a race: another request inserted this device. Reload and update it.
            _context.ChangeTracker.Clear();
            var concurrent = await _context.UserDevices
                .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceToken == deviceToken);
            if (concurrent != null)
            {
                concurrent.Platform = platform;
                await _context.SaveChangesAsync();
            }
            _logger.LogInformation("Concurrent device registration resolved for User {UserId}", userId);
        }

        _logger.LogInformation("Registered push device for User {UserId} Platform {Platform}", userId, platform);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // PostgreSQL SQLSTATE 23505 = unique_violation. Checked via the exception name to
        // avoid a hard compile-time dependency on the Npgsql type here.
        var inner = ex.InnerException;
        return inner is not null
            && inner.GetType().Name == "PostgresException"
            && (inner.Data["SqlState"] as string == "23505"
                || inner.Message.Contains("23505")
                || inner.Message.Contains("duplicate key"));
    }

    // Types that should never be shown as user-configurable preferences.
    private static readonly HashSet<NotificationType> HiddenPreferenceTypes = new()
    {
        NotificationType.FriendRequest, // Deprecated
        // Account & system notifications are essential and always delivered.
        NotificationType.System,
        NotificationType.VerificationResult,
        NotificationType.BusinessClaimResult,
    };

    public async Task<List<NotificationPreferenceDto>> GetPreferencesAsync(string userId)
    {
        var stored = await _context.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Type, p => p.IsEnabled);

        // Return every configurable notification type, merging in any stored
        // overrides. Types without a stored row default to enabled. This means
        // a brand-new user still sees the full list of toggles.
        return Enum.GetValues<NotificationType>()
            .Where(type => !HiddenPreferenceTypes.Contains(type))
            .Select(type => new NotificationPreferenceDto(
                type,
                type.ToString(),
                stored.TryGetValue(type, out var isEnabled) ? isEnabled : true))
            .ToList();
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

