using Ping.Models;
using Ping.Models.Notifications;
using Ping.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Ping.Dtos.Notifications;
using Asp.Versioning;

namespace Ping.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<ActionResult<Ping.Dtos.Common.PaginatedResult<Ping.Dtos.Notifications.NotificationDto>>> GetNotifications([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var notifications = await _notificationService.GetNotificationsAsync(userId, pageNumber, pageSize);
        return Ok(notifications);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<Ping.Dtos.Notifications.UnreadCountDto>> GetUnreadCount()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var count = await _notificationService.GetUnreadCountAsync(userId);
        return Ok(new Ping.Dtos.Notifications.UnreadCountDto(count));
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(string id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _notificationService.MarkAsReadAsync(userId, id);
        return Ok();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _notificationService.MarkAllAsReadAsync(userId);
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteNotification(string id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _notificationService.DeleteNotificationAsync(userId, id);
        return Ok();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAllNotifications()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _notificationService.DeleteAllNotificationsAsync(userId);
        return Ok();
    }

    [HttpPost("register-device")]
    public async Task<IActionResult> RegisterDevice(RegisterDeviceDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _notificationService.RegisterDeviceAsync(userId, dto.DeviceToken, dto.Platform);
        return Ok();
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<List<NotificationPreferenceDto>>> GetPreferences()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var prefs = await _notificationService.GetPreferencesAsync(userId);
        return Ok(prefs);
    }

    [HttpPatch("preferences")]
    public async Task<IActionResult> UpdatePreference(UpdateNotificationPreferenceDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        await _notificationService.UpdatePreferenceAsync(userId, dto.Type, dto.IsEnabled);
        return Ok();
    }
}
