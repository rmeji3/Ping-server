using Conquest.Data.App;
using Conquest.Dtos.Activities;
using Conquest.Dtos.Places;
using Conquest.Models.Places;
using Conquest.Services.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.Places;

public class PlaceService(
    AppDbContext db, 
    IRedisService redis,
    IConfiguration configuration,
    ILogger<PlaceService> logger) : IPlaceService
{
    public async Task<PlaceDetailsDto> CreatePlaceAsync(UpsertPlaceDto dto, string userId)
    {
        // Redis-based daily rate limit per user
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var rateLimitKey = $"ratelimit:place:create:{userId}:{today}";
        var limit = configuration.GetValue<int>("RateLimiting:PlaceCreationLimitPerDay", 10);

        // Increment counter with 24-hour expiry
        var createdToday = await redis.IncrementAsync(rateLimitKey, TimeSpan.FromHours(24));

        if (createdToday > limit)
        {
            logger.LogWarning("Place creation rate limit reached for user {UserId}. Count: {Count}", userId, createdToday);
            throw new InvalidOperationException("You've reached the daily limit for adding places.");
        }

        var place = new Place
        {
            Name = dto.Name.Trim(),
            Address = dto.Address.Trim(),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            OwnerUserId = userId,
            IsPublic = dto.IsPublic,
            CreatedUtc = DateTime.UtcNow
        };

        db.Places.Add(place);
        await db.SaveChangesAsync();

        logger.LogInformation("Place created: {PlaceId} by {UserId}. Daily count: {Count}/{Limit}", 
            place.Id, userId, createdToday, limit);

        return new PlaceDetailsDto(
            place.Id,
            place.Name,
            place.Address ?? string.Empty,
            place.Latitude,
            place.Longitude,
            place.IsPublic,
            IsOwner: true,
            IsFavorited: false,
            Activities: Array.Empty<ActivitySummaryDto>(),
            ActivityKinds: Array.Empty<string>()
        );
    }

    public async Task<PlaceDetailsDto?> GetPlaceByIdAsync(int id, string? userId)
    {
        var p = await db.Places
            .AsNoTracking()
            .Include(x => x.PlaceActivities)
                .ThenInclude(pa => pa.ActivityKind)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p is null) return null;

        var isOwner = userId != null && p.OwnerUserId == userId;

        // Hide private places from non-owners
        if (!p.IsPublic && !isOwner)
            return null;

        // Check if favorited by current user
        var isFavorited = userId != null && await db.Favorited
            .AnyAsync(f => f.UserId == userId && f.PlaceId == id);

        var activities = p.PlaceActivities
            .Select(a => new ActivitySummaryDto(
                a.Id,
                a.Name,
                a.ActivityKindId,
                a.ActivityKind?.Name
            ))
            .ToArray();

        var activityKindNames = p.PlaceActivities
            .Where(a => a.ActivityKind != null)
            .Select(a => a.ActivityKind!.Name)
            .Distinct()
            .ToArray();

        return new PlaceDetailsDto(
            p.Id,
            p.Name,
            p.Address ?? string.Empty,
            p.Latitude,
            p.Longitude,
            p.IsPublic,
            isOwner,
            isFavorited,
            activities,
            activityKindNames
        );
    }

    public async Task<IEnumerable<PlaceDetailsDto>> SearchNearbyAsync(double lat, double lng, double radiusKm, string? activityName, string? activityKind, string? userId)
    {
        logger.LogDebug("Nearby search: lat={Lat}, lng={Lng}, radius={Radius}", lat, lng, radiusKm);

        var latDelta = radiusKm / 111.0;
        var lngDelta = radiusKm / (111.0 * Math.Cos(lat * Math.PI / 180.0));
        var minLat = lat - latDelta;
        var maxLat = lat + latDelta;
        var minLng = lng - lngDelta;
        var maxLng = lng + lngDelta;

        var q = db.Places
            .Where(p => p.Latitude >= minLat && p.Latitude <= maxLat &&
                        p.Longitude >= minLng && p.Longitude <= maxLng)
            .Where(p => p.IsPublic || (userId != null && p.OwnerUserId == userId))
            .AsNoTracking()
            .Include(p => p.PlaceActivities)
                .ThenInclude(pa => pa.ActivityKind)
            .AsQueryable();

        // Filter by ACTIVITY NAME (PlaceActivity.Name)
        if (!string.IsNullOrWhiteSpace(activityName))
        {
            var an = activityName.Trim().ToLowerInvariant();
            q = q.Where(p =>
                p.PlaceActivities.Any(a =>
                    a.Name.ToLower() == an));
        }

        // Filter by ACTIVITY KIND (ActivityKind.Name)
        if (!string.IsNullOrWhiteSpace(activityKind))
        {
            var ak = activityKind.Trim().ToLowerInvariant();
            q = q.Where(p =>
                p.PlaceActivities.Any(a =>
                    a.ActivityKind != null &&
                    a.ActivityKind.Name.ToLower() == ak));
        }

        var list = await q
            .Select(p => new
            {
                p,
                DistanceKm = 6371.0 * 2.0 * Math.Asin(
                    Math.Sqrt(
                        Math.Pow(Math.Sin((p.Latitude - lat) * Math.PI / 180.0 / 2.0), 2) +
                        Math.Cos(lat * Math.PI / 180.0) * Math.Cos(p.Latitude * Math.PI / 180.0) *
                        Math.Pow(Math.Sin((p.Longitude - lng) * Math.PI / 180.0 / 2.0), 2)
                    )
                )
            })
            .Where(x => x.DistanceKm <= radiusKm)
            .OrderBy(x => x.DistanceKm)
            .Take(100)
            .ToListAsync();

        // Batch check which places are favorited by the current user
        var placeIds = list.Select(x => x.p.Id).ToList();
        var favoritedPlaceIds = new HashSet<int>();
        
        if (userId != null)
        {
            favoritedPlaceIds = await db.Favorited
                .Where(f => f.UserId == userId && placeIds.Contains(f.PlaceId))
                .Select(f => f.PlaceId)
                .ToHashSetAsync();
        }

        return list.Select(x =>
        {
            var activityKindNames = x.p.PlaceActivities
                .Where(a => a.ActivityKind != null)
                .Select(a => a.ActivityKind!.Name)
                .Distinct()
                .ToArray();

            var isOwner = userId != null && x.p.OwnerUserId == userId;
            var isFavorited = favoritedPlaceIds.Contains(x.p.Id);

            var activities = x.p.PlaceActivities
                .Select(a => new ActivitySummaryDto(
                    a.Id,
                    a.Name,
                    a.ActivityKindId,
                    a.ActivityKind?.Name
                ))
                .ToArray();
            
            return new PlaceDetailsDto(
                x.p.Id,
                x.p.Name,
                x.p.Address ?? string.Empty,
                x.p.Latitude,
                x.p.Longitude,
                x.p.IsPublic,
                isOwner,
                isFavorited,
                activities,
                activityKindNames
            );
        }).ToList();
    }

    public async Task AddFavoriteAsync(int id, string userId)
    {
        // Check if already favorited
        var exists = await db.Favorited
            .AnyAsync(f => f.UserId == userId && f.PlaceId == id);
        
        if (exists)
        {
            logger.LogInformation("Place {PlaceId} already favorited by {UserId}", id, userId);
            return;
        }
        
        // Check if place exists
        var placeExists = await db.Places.AnyAsync(p => p.Id == id);
        if (!placeExists)
        {
            throw new InvalidOperationException("Place not found.");
        }
        
        var favorited = new Favorited
        {
            UserId = userId,
            PlaceId = id
        };
        await db.Favorited.AddAsync(favorited);
        await db.SaveChangesAsync();

        logger.LogInformation("Place {PlaceId} favorited by {UserId}", id, userId);
    }

    public async Task UnfavoriteAsync(int id, string userId)
    {
        var favorited = await db.Favorited
            .FirstOrDefaultAsync(f => f.UserId == userId && f.PlaceId == id);
        
        if (favorited != null)
        {
            db.Favorited.Remove(favorited);
            await db.SaveChangesAsync();
            logger.LogInformation("Place {PlaceId} unfavorited by {UserId}", id, userId);
        }
    }

    public async Task<IEnumerable<PlaceDetailsDto>> GetFavoritedPlacesAsync(string userId)
    {
        var favorites = await db.Favorited
            .Where(f => f.UserId == userId)
            .Include(f => f.Place)
                .ThenInclude(p => p.PlaceActivities)
                    .ThenInclude(pa => pa.ActivityKind)
            .AsNoTracking()
            .ToListAsync();

        var list = favorites.Select(f =>
        {
            var activities = f.Place.PlaceActivities
                .Select(a => new ActivitySummaryDto(
                    a.Id,
                    a.Name,
                    a.ActivityKindId,
                    a.ActivityKind?.Name
                ))
                .ToArray();

            var activityKindNames = f.Place.PlaceActivities
                .Where(a => a.ActivityKind != null)
                .Select(a => a.ActivityKind!.Name)
                .Distinct()
                .ToArray();

            var isOwner = f.Place.OwnerUserId == userId;

            return new PlaceDetailsDto(
                f.Place.Id,
                f.Place.Name,
                f.Place.Address ?? string.Empty,
                f.Place.Latitude,
                f.Place.Longitude,
                f.Place.IsPublic,
                isOwner,
                IsFavorited: true,
                activities,
                activityKindNames
            );
        }).ToList();

        logger.LogInformation("Favorited places for {UserId} retrieved: {Count} places", userId, list.Count);

        return list;
    }
}
