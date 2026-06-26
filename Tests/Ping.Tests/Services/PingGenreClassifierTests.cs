using Xunit;
using Moq;
using Ping.Data.App;
using Ping.Services.AI;
using Ping.Services.Google;
using Ping.Services.Background;
using Ping.Models.Pings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace Ping.Tests.Services;

public class PingGenreClassifierTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IPingNameService> _mockPingNameService;
    private readonly Mock<ISemanticService> _mockSemanticService;
    private readonly Mock<ILogger<PingGenreClassifier>> _mockLogger;
    private readonly PingGenreClassifier _classifier;

    public PingGenreClassifierTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PingGenreTestDb_{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _mockPingNameService = new Mock<IPingNameService>();
        _mockSemanticService = new Mock<ISemanticService>();
        _mockLogger = new Mock<ILogger<PingGenreClassifier>>();

        _classifier = new PingGenreClassifier(
            _db,
            _mockPingNameService.Object,
            _mockSemanticService.Object,
            _mockLogger.Object
        );
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task ClassifyAsync_ShouldNoOp_IfPingDoesNotExist()
    {
        // Arrange
        var job = new PingGenreJob(PingId: 999, PingName: "Non-existent", GooglePlaceId: "some-id");

        // Act
        await _classifier.ClassifyAsync(job, CancellationToken.None);

        // Assert
        _mockPingNameService.Verify(x => x.GetGooglePlaceTypesAsync(It.IsAny<string>()), Times.Never);
        _mockSemanticService.Verify(x => x.ClassifyGenreAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task ClassifyAsync_ShouldNoOp_IfPingAlreadyHasGenre()
    {
        // Arrange
        var ping = new Models.Pings.Ping
        {
            Id = 1,
            Name = "Gym workout",
            Location = new NetTopologySuite.Geometries.Point(-87.6298, 41.8781) { SRID = 4326 },
            PingGenreId = 7, // Wellness
            OwnerUserId = "user-123"
        };
        _db.Pings.Add(ping);
        await _db.SaveChangesAsync();

        var job = new PingGenreJob(PingId: ping.Id, PingName: ping.Name, GooglePlaceId: "gym-place-id");

        // Act
        await _classifier.ClassifyAsync(job, CancellationToken.None);

        // Assert
        _mockPingNameService.Verify(x => x.GetGooglePlaceTypesAsync(It.IsAny<string>()), Times.Never);
        _mockSemanticService.Verify(x => x.ClassifyGenreAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task ClassifyAsync_ShouldUseTier1_WhenGooglePlaceTypesMatch()
    {
        // Arrange
        var ping = new Models.Pings.Ping
        {
            Id = 2,
            Name = "Planet Fitness",
            Location = new NetTopologySuite.Geometries.Point(-87.6298, 41.8781) { SRID = 4326 },
            PingGenreId = null,
            OwnerUserId = "user-123"
        };
        _db.Pings.Add(ping);
        await _db.SaveChangesAsync();

        _mockPingNameService
            .Setup(x => x.GetGooglePlaceTypesAsync("gym-place-id"))
            .ReturnsAsync(new List<string> { "gym", "health" });

        var job = new PingGenreJob(PingId: ping.Id, PingName: ping.Name, GooglePlaceId: "gym-place-id");

        // Act
        await _classifier.ClassifyAsync(job, CancellationToken.None);

        // Assert
        var updatedPing = await _db.Pings.FindAsync(ping.Id);
        Assert.NotNull(updatedPing);
        Assert.Equal(7, updatedPing.PingGenreId); // Wellness (gym is mapped to Wellness, ID = 7)

        _mockSemanticService.Verify(x => x.ClassifyGenreAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task ClassifyAsync_ShouldFallBackToTier2_WhenGooglePlaceTypesDoNotMatch()
    {
        // Arrange
        var ping = new Models.Pings.Ping
        {
            Id = 3,
            Name = "My Custom Workspace",
            Location = new NetTopologySuite.Geometries.Point(-87.6298, 41.8781) { SRID = 4326 },
            PingGenreId = null,
            OwnerUserId = "user-123"
        };
        _db.Pings.Add(ping);
        await _db.SaveChangesAsync();

        _mockPingNameService
            .Setup(x => x.GetGooglePlaceTypesAsync("unknown-place-id"))
            .ReturnsAsync(new List<string> { "unknown_type" });

        _mockSemanticService
            .Setup(x => x.ClassifyGenreAsync("My Custom Workspace", null, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync("Work");

        var job = new PingGenreJob(PingId: ping.Id, PingName: ping.Name, GooglePlaceId: "unknown-place-id");

        // Act
        await _classifier.ClassifyAsync(job, CancellationToken.None);

        // Assert
        var updatedPing = await _db.Pings.FindAsync(ping.Id);
        Assert.NotNull(updatedPing);
        Assert.Equal(10, updatedPing.PingGenreId); // Work (ID = 10)

        _mockPingNameService.Verify(x => x.GetGooglePlaceTypesAsync("unknown-place-id"), Times.Once);
        _mockSemanticService.Verify(x => x.ClassifyGenreAsync("My Custom Workspace", null, It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Fact]
    public async Task ClassifyAsync_ShouldUseTier2Immediately_WhenGooglePlaceIdIsEmpty()
    {
        // Arrange
        var ping = new Models.Pings.Ping
        {
            Id = 4,
            Name = "Study group at Library",
            Location = new NetTopologySuite.Geometries.Point(-87.6298, 41.8781) { SRID = 4326 },
            PingGenreId = null,
            OwnerUserId = "user-123"
        };
        _db.Pings.Add(ping);
        await _db.SaveChangesAsync();

        _mockSemanticService
            .Setup(x => x.ClassifyGenreAsync("Study group at Library", null, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync("Education");

        var job = new PingGenreJob(PingId: ping.Id, PingName: ping.Name, GooglePlaceId: "");

        // Act
        await _classifier.ClassifyAsync(job, CancellationToken.None);

        // Assert
        var updatedPing = await _db.Pings.FindAsync(ping.Id);
        Assert.NotNull(updatedPing);
        Assert.Equal(9, updatedPing.PingGenreId); // Education (ID = 9)

        _mockPingNameService.Verify(x => x.GetGooglePlaceTypesAsync(It.IsAny<string>()), Times.Never);
        _mockSemanticService.Verify(x => x.ClassifyGenreAsync("Study group at Library", null, It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Fact]
    public async Task ClassifyAsync_ShouldBypassTier1_WhenActivityIsPresent()
    {
        // Arrange
        var ping = new Models.Pings.Ping
        {
            Id = 5,
            Name = "Planet Fitness",
            Location = new NetTopologySuite.Geometries.Point(-87.6298, 41.8781) { SRID = 4326 },
            PingGenreId = null,
            OwnerUserId = "user-123"
        };
        _db.Pings.Add(ping);

        var activity = new PingActivity
        {
            Id = 10,
            PingId = 5,
            Name = "Basketball",
            CreatedUtc = DateTime.UtcNow
        };
        _db.PingActivities.Add(activity);
        await _db.SaveChangesAsync();

        _mockSemanticService
            .Setup(x => x.ClassifyGenreAsync("Planet Fitness", "Basketball", It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync("Sports");

        var job = new PingGenreJob(PingId: ping.Id, PingName: ping.Name, GooglePlaceId: "gym-place-id");

        // Act
        await _classifier.ClassifyAsync(job, CancellationToken.None);

        // Assert
        var updatedPing = await _db.Pings.FindAsync(ping.Id);
        Assert.NotNull(updatedPing);
        Assert.Equal(1, updatedPing.PingGenreId); // Sports (ID = 1)

        _mockPingNameService.Verify(x => x.GetGooglePlaceTypesAsync(It.IsAny<string>()), Times.Never);
        _mockSemanticService.Verify(x => x.ClassifyGenreAsync("Planet Fitness", "Basketball", It.IsAny<IEnumerable<string>>()), Times.Once);
    }
}
