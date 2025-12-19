using Ping.Data.App;
using Ping.Dtos.Pings;
using Ping.Models.Pings;
using Ping.Services.Pings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace Ping.Tests;

public class SpatialTest : BaseIntegrationTest
{
    public SpatialTest(IntegrationTestFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreatePlace_StoresLocationAndProxiesLatLong()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IPingService>();
        var userId = "test_user_spatial_1";

        // Act
        var created = await service.CreatePingAsync(new UpsertPingDto(
            "Central Park Test",
            "Address",
            40.785091, 
            -73.968285,
            PingVisibility.Public,
            PingType.Custom,
            null // PingGenreId
        ), userId);

        // Assert
        var place = await db.Pings.FindAsync(created.Id);
        Assert.NotNull(place);
        Assert.NotNull(place.Location);
        Assert.Equal(40.785091, place.Location.Y); // Lat
        Assert.Equal(-73.968285, place.Location.X); // Lng
        Assert.Equal(40.785091, place.Latitude);
        Assert.Equal(-73.968285, place.Longitude);
    }

    [Fact]
    public async Task SearchNearby_FiltersCorrectly()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IPingService>();
        var userId = "test_user_spatial_2";

        // Create specific places
        // 1. Target: Times Square
        var timesSquare = new Models.Pings.Ping
        {
            Name = "Times Square",
            Latitude = 40.7580,
            Longitude = -73.9855,
            OwnerUserId = userId,
            Visibility = PingVisibility.Public,
            Type = PingType.Custom,
            Location = new Point(-73.9855, 40.7580) { SRID = 4326 }
        };

        // 2. Far away: London
        var london = new Models.Pings.Ping
        {
            Name = "London Eye",
            Latitude = 51.5033,
            Longitude = -0.1195,
            OwnerUserId = userId,
            Visibility = PingVisibility.Public,
            Type = PingType.Custom,
            Location = new Point(-0.1195, 51.5033) { SRID = 4326 }
        };

        db.Pings.AddRange(timesSquare, london);
        await db.SaveChangesAsync();

        // Act
        // Search near Times Square with 1km radius
        var result = await service.SearchNearbyAsync(
            40.7580, 
            -73.9855, 
            1.0, 
            null, null, null, null, null, 
            new Dtos.Common.PaginationParams { PageNumber = 1, PageSize = 10 }
        );

        // Assert
        Assert.Contains(result.Items, p => p.Name == "Times Square");
        Assert.DoesNotContain(result.Items, p => p.Name == "London Eye");
    }
}

