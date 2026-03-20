using Ping.Data.App;
using Ping.Models.Notifications;
using Ping.Models.Events;
using Ping.Models;
using Ping.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ping.Services.Background;

public class EventReminderBackgroundService(
    IServiceProvider services,
    ILogger<EventReminderBackgroundService> logger) : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Event Reminder Background Service starting.");

        using var timer = new PeriodicTimer(_checkInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SendEventRemindersAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Event Reminder Background Service is stopping.");
        }
    }

    private async Task SendEventRemindersAsync(CancellationToken token)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var now = DateTime.UtcNow;
            var soon = now.AddMinutes(40); // Slightly more than checkInterval + leadTime to ensure overlap

            // Find attendees who haven't been notified yet for events starting soon
            var reminders = await db.EventAttendees
                .Include(a => a.Event)
                .Where(a => a.Status == AttendeeStatus.Attending && 
                            !a.ReminderSent &&
                            a.Event.StartTime > now && 
                            a.Event.StartTime <= soon)
                .ToListAsync(token);

            if (reminders.Count == 0) return;

            logger.LogInformation("Found {Count} event reminders to send.", reminders.Count);

            foreach (var attendee in reminders)
            {
                try
                {
                    await notificationService.SendNotificationAsync(new Notification
                    {
                        UserId = attendee.UserId,
                        Type = NotificationType.EventStartsSoon,
                        Title = "Event Starting Soon!",
                        Message = $"The event '{attendee.Event.Title}' is starting soon. Don't be late!",
                        ReferenceId = attendee.EventId.ToString(),
                        ImageThumbnailUrl = attendee.Event.ThumbnailUrl ?? attendee.Event.ImageUrl
                    });

                    attendee.ReminderSent = true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send event reminder to {UserId} for event {EventId}", attendee.UserId, attendee.EventId);
                }
            }

            await db.SaveChangesAsync(token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during event reminder task.");
        }
    }
}
