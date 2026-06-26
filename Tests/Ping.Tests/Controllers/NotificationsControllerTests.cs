using System.Net.Http.Json;
using System.Security.Claims;
using Ping.Data.App;
using Ping.Dtos.Notifications;
using Ping.Models;
using Ping.Models.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ping.Tests.Controllers;

public class NotificationsControllerTests : BaseIntegrationTest
{
    // Minimal shape for deserializing the endpoint's PaginatedResult JSON. The real
    // PaginatedResult<T> can't be read by System.Text.Json (its ctor param 'count'
    // doesn't match the 'TotalCount' property), and we only need Items here.
    private record NotificationPage(List<NotificationDto> Items);

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

        // The endpoint returns a paginated wrapper of NotificationDto, not a bare array.
        var result = await response.Content.ReadFromJsonAsync<NotificationPage>(options);
        Assert.NotNull(result);
        Assert.NotEmpty(result!.Items);
        Assert.Equal("Test Notification", result.Items[0].Title);
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

