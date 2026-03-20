using System.Net.Http.Json;
using System.Security.Claims;
using Ping.Data.App;
using Ping.Models;
using Ping.Models.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ping.Tests.Controllers;

public class NotificationsControllerTests : BaseIntegrationTest
{
    public NotificationsControllerTests(IntegrationTestFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetNotifications_ShouldReturnList()
    {
        // Arrange
        var userId = Authenticate("user1");
        
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Notifications.Add(new Notification 
            { 
                UserId = userId, 
                Title = "Test Notification", 
                Type = NotificationType.System 
            });
            await context.SaveChangesAsync();
        }

        // Act
        var response = await Client.GetAsync("/api/notifications");

        // Assert
        response.EnsureSuccessStatusCode();
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        var notifications = await response.Content.ReadFromJsonAsync<List<Notification>>(options);
        Assert.NotNull(notifications);
        Assert.NotEmpty(notifications);
        Assert.Equal("Test Notification", notifications[0].Title);
    }

    [Fact]
    public async Task MarkAsRead_ShouldUpdateStatus()
    {
        // Arrange
        var userId = Authenticate("user1");
        string notifId;

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var n = new Notification 
            { 
                UserId = userId, 
                Title = "Unread", 
                IsRead = false,
                Type = NotificationType.System 
            };
            context.Notifications.Add(n);
            await context.SaveChangesAsync();
            notifId = n.Id;
        }

        // Act
        var response = await Client.PostAsync($"/api/notifications/{notifId}/read", null);

        // Assert
        response.EnsureSuccessStatusCode();

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updated = await context.Notifications.FindAsync(notifId);
            Assert.NotNull(updated);
            Assert.True(updated.IsRead);
        }
    }
}

