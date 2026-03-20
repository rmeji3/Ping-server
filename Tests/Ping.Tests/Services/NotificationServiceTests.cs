using Ping.Data.App;
using Ping.Models;
using Ping.Models.Notifications;
using Ping.Services.Notifications;
using Ping.Services.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Amazon.SimpleNotificationService;

namespace Ping.Tests.Services;

public class NotificationServiceTests
{
    private readonly Mock<IRedisService> _mockRedis;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<NotificationService>> _mockLogger;
    private readonly Mock<IAmazonSimpleNotificationService> _mockSns;
    private readonly AppDbContext _context;
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _mockRedis = new Mock<IRedisService>();
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<NotificationService>>();
        _mockSns = new Mock<IAmazonSimpleNotificationService>();

        // Config setup
        var mockSectionFriend = new Mock<IConfigurationSection>();
        mockSectionFriend.Setup(x => x.Value).Returns("1");
        
        var mockSectionReview = new Mock<IConfigurationSection>();
        mockSectionReview.Setup(x => x.Value).Returns("3");

        _mockConfig.Setup(c => c.GetSection("NotificationRateLimits:FriendRequestLimitPer12Hours")).Returns(mockSectionFriend.Object);
        _mockConfig.Setup(c => c.GetSection("NotificationRateLimits:ReviewLikeLimitPerHour")).Returns(mockSectionReview.Object);

        // DbContext setup
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);

        _service = new NotificationService(_context, _mockRedis.Object, _mockConfig.Object, _mockLogger.Object, _mockSns.Object);
    }

    [Fact]
    public async Task SendNotificationAsync_ShouldSaveNotification_WhenNotRateLimited()
    {
        // Arrange
        var notification = new Notification
        {
            UserId = "user1",
            SenderId = "sender1",
            Type = NotificationType.FriendRequest,
            Title = "Test",
            Message = "Test Message"
        };

        // Redis returns 1 (first request)
        _mockRedis.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(1);

        // Act
        await _service.SendNotificationAsync(notification);

        // Assert
        var saved = await _context.Notifications.FirstOrDefaultAsync();
        Assert.NotNull(saved);
        Assert.Equal("Test", saved.Title);
    }

    [Fact]
    public async Task SendNotificationAsync_ShouldDropNotification_WhenRateLimited()
    {
        // Arrange
        var notification = new Notification
        {
            UserId = "user1",
            SenderId = "sender1",
            Type = NotificationType.FriendRequest, // Limit is 1
            Title = "Spam",
            Message = "Spam Message"
        };

        // Redis returns 2 (limit exceeded)
        _mockRedis.Setup(x => x.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(2);

        // Act
        await _service.SendNotificationAsync(notification);

        // Assert
        var count = await _context.Notifications.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        _context.Notifications.Add(new Notification { UserId = "u1", IsRead = false, Title="1", Type=NotificationType.System });
        _context.Notifications.Add(new Notification { UserId = "u1", IsRead = true, Title="2", Type=NotificationType.System });
        _context.Notifications.Add(new Notification { UserId = "u1", IsRead = false, Title="3", Type=NotificationType.System });
        _context.Notifications.Add(new Notification { UserId = "u2", IsRead = false, Title="4", Type=NotificationType.System });
        await _context.SaveChangesAsync();

        // Act
        var count = await _service.GetUnreadCountAsync("u1");

        // Assert
        Assert.Equal(2, count);
    }
}

