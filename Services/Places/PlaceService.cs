using Conquest.Data.App;
using Conquest.Dtos.Activities;
using Conquest.Dtos.Places;
using Conquest.Models.Places;
using Conquest.Models.Reviews;
using Conquest.Services.Friends;
using Conquest.Services.Google;
using Conquest.Services.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.Places;

public class PlaceService(
    AppDbContext db,
    IRedisService redis,
    IConfiguration config,
    IPlaceNameService placeNameService,
    IFriendService friendService,
    Services.Moderation.IModerationService moderationService,
    ILogger<PlaceService> logger) : IPlaceService
{
    public async Task<PlaceDetailsDto> CreatePlaceAsync(UpsertPlaceDto dto, string userId)
    {
        // Redis-based daily rate limit per user
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var rateLimitKey = $"ratelimit:place:create:{userId}:{today}";
        var limit = config.GetValue<int>("RateLimiting:PlaceCreationLimitPerDay", 10);

        // Increment counter with 24-hour expiry
        var createdToday = await redis.IncrementAsync(rateLimitKey, TimeSpan.FromHours(24));

        if (createdToday > limit)
        {
            logger.LogWarning("Place creation rate limit reached for user {UserId}. Count: {Count}", userId, createdToday);
            throw new InvalidOperationException("You've reached the daily limit for adding places.");
        }

        // RULE: Private/Friends places can only be Custom (not Verified)
        // Verified places are only for Public places to avoid unnecessary Google API calls
        if (dto.Visibility != PlaceVisibility.Public && dto.Type == PlaceType.Verified)
        {
            logger.LogWarning("Verified type is only allowed for Public places. Auto-correcting to Custom for {Visibility} place.", dto.Visibility);
            dto = dto with { Type = PlaceType.Custom };
        }

        // Logic:
        // 1. If Visibility is Public -> Check for existing Public place based on Type
        //    - Verified: Check by ADDRESS only (no coordinate check)
        //    - Custom: Check by COORDINATES only (~50m tolerance)
        // 2. If Visibility is Private/Friends -> Always create new (Allow duplicates)

        var finalName = dto.Name.Trim();

        // Moderate Place Name (only if User Provided)
        // If Verified/Google provided, we might trust it? But safest to just check all if user can edit it.
        // User PROVIDES the name in dto.Name initially, even if we later overwrite it with Google Name.
        // But if they use PlaceType.Custom, they set the name.
        if (dto.Type == PlaceType.Custom)
        {
             var mod = await moderationService.CheckContentAsync(finalName);
             if (mod.IsFlagged)
             {
                 logger.LogWarning("Place name flagged: {Name} - {Reason}", finalName, mod.Reason);
                 throw new ArgumentException($"Place name rejected: {mod.Reason}");
             }
        }

        if (dto.Visibility == PlaceVisibility.Public)
        {
            if (dto.Type == PlaceType.Verified)
            {
                // VERIFIED PLACE: Requires address, check by address ONLY
                if (string.IsNullOrWhiteSpace(dto.Address))
                {
                    logger.LogError("Verified place creation failed: Address is required for verified places.");
                    throw new InvalidOperationException("Verified places require an address.");
                }

                // Duplicate check: BY ADDRESS ONLY (no coordinate checking)
                var existingByAddress = await db.Places
                    .Where(p => p.Visibility == PlaceVisibility.Public &&
                                p.Type == PlaceType.Verified &&
                                p.Address == dto.Address.Trim())
                    .FirstOrDefaultAsync();

                if (existingByAddress != null)
                {
                    logger.LogInformation("Verified place already exists with address '{Address}'. Returning existing place {PlaceId}.", dto.Address, existingByAddress.Id);
                    return await ToPlaceDetailsDto(existingByAddress, userId);
                }

                // Fetch Google name for verified places
                logger.LogInformation("Verified place detected. Calling Google Places API for coordinates {Lat}, {Lng}", dto.Latitude, dto.Longitude);
                var googleName = await placeNameService.GetPlaceNameAsync(dto.Latitude, dto.Longitude);
                
                if (!string.IsNullOrWhiteSpace(googleName))
                {
                    logger.LogInformation("Using Google Places name: '{GoogleName}' for verified place.", googleName);
                    finalName = googleName;
                }
                else
                {
                    logger.LogInformation("Google Places returned no name. Using user-provided name: '{UserName}'", finalName);
                }
            }
            else // PlaceType.Custom
            {
                // CUSTOM PLACE: Coordinates-based, wider tolerance (~50m)
                // Duplicate check: BY COORDINATES ONLY
                // 0.0005 degrees ~= 50-55 meters
                var existingCustom = await db.Places
                    .Where(p => p.Visibility == PlaceVisibility.Public &&
                                p.Type == PlaceType.Custom &&
                                Math.Abs(p.Latitude - dto.Latitude) < 0.0005 &&
                                Math.Abs(p.Longitude - dto.Longitude) < 0.0005)
                    .FirstOrDefaultAsync();

                if (existingCustom != null)
                {
                    logger.LogInformation("Custom place already exists within 50m at coordinates {Lat}, {Lng}. Returning existing place {PlaceId}.", dto.Latitude, dto.Longitude, existingCustom.Id);
                    return await ToPlaceDetailsDto(existingCustom, userId);
                }

                // Use user-provided name for custom places (no Google API call)
                logger.LogInformation("Custom place created with user-provided name: '{UserName}'", finalName);
            }
        }
        else
        {
            logger.LogInformation("Non-public place (Visibility: {Visibility}). Skipping duplicate checks. Using user name: '{UserName}'", dto.Visibility, finalName);
        }

        var place = new Place
        {
            Name = finalName,
            Address = dto.Address?.Trim() ?? string.Empty,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            OwnerUserId = userId,
            Visibility = dto.Visibility,
            Type = dto.Type,
            CreatedUtc = DateTime.UtcNow
        };

        db.Places.Add(place);
        await db.SaveChangesAsync();

        logger.LogInformation("Place created: {PlaceId} by {UserId}. Visibility: {Visibility}. Daily count: {Count}/{Limit}", 
            place.Id, userId, dto.Visibility, createdToday, limit);

        return await ToPlaceDetailsDto(place, userId);
    }

    public async Task<PlaceDetailsDto?> GetPlaceByIdAsync(int id, string? userId)
    {
        var p = await db.Places
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.PlaceActivities)
                .ThenInclude(pa => pa.ActivityKind)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p is null) return null;

        var isOwner = userId != null && p.OwnerUserId == userId;

        // Visibility Check
        if (!isOwner)
        {
            if (p.Visibility == PlaceVisibility.Private)
                return null; // Private places only visible to owner

            if (p.Visibility == PlaceVisibility.Friends && userId != null)
            {
                // Check if friend
                var friendIds = await friendService.GetFriendIdsAsync(userId);
                var isFriend = friendIds.Contains(p.OwnerUserId);
                if (!isFriend) return null;
            }
            else if (p.Visibility == PlaceVisibility.Friends && userId == null)
            {
                return null; // Anonymous users can't see Friends-only places
            }
        }

        return await ToPlaceDetailsDto(p, userId);
    }
    private static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLng = (lng2 - lng1) * Math.PI / 180.0;

        lat1 *= Math.PI / 180.0;
        lat2 *= Math.PI / 180.0;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2) * Math.Cos(lat1) * Math.Cos(lat2);

        var c = 2 * Math.Asin(Math.Sqrt(a));
        return 6371.0 * c;
    }
    private static bool IsVisibleToUser(Place p, string? userId, HashSet<string> friendIds)
    {
        var isOwner = userId != null && p.OwnerUserId == userId;

        if (isOwner) return true;

        return p.Visibility switch
        {
            PlaceVisibility.Public  => true,
            PlaceVisibility.Private => false,
            PlaceVisibility.Friends => userId != null && friendIds.Contains(p.OwnerUserId),
            _ => false
        };
    }


    public async Task<IEnumerable<PlaceDetailsDto>> SearchNearbyAsync(
        double lat,
        double lng,
        double radiusKm,
        string? activityName,
        string? activityKind,
        PlaceVisibility? visibility,
        PlaceType? type,
        string? userId
    )
    {
        logger.LogDebug("Nearby search: lat={Lat}, lng={Lng}, radius={Radius}, vis={Vis}, type={Type}", lat, lng, radiusKm, visibility, type);

        var q = db.Places
        .Where(p => !p.IsDeleted)
        .Include(p => p.PlaceActivities)
            .ThenInclude(pa => pa.ActivityKind)
        .AsNoTracking();

        // Filter by Visibility
        if (visibility.HasValue)
        {
            q = q.Where(p => p.Visibility == visibility.Value);
        }

        // Filter by PlaceType
        if (type.HasValue)
        {
            q = q.Where(p => p.Type == type.Value);
        }

        // Filter by ACTIVITY NAME
        if (!string.IsNullOrWhiteSpace(activityName))
        {
            var an = activityName.Trim().ToLowerInvariant();
            q = q.Where(p => p.PlaceActivities.Any(a => a.Name != null && a.Name.ToLower() == an));
        }

        // Filter by ACTIVITY KIND
        if (!string.IsNullOrWhiteSpace(activityKind))
        {
            var ak = activityKind.Trim().ToLowerInvariant();
            q = q.Where(p => p.PlaceActivities.Any(a => 
                a.ActivityKind != null &&  
                a.ActivityKind.Name != null && 
                a.ActivityKind.Name.ToLower() == ak
            ));
        }

        var candidates = await q.ToListAsync();

        // Get Friend List if needed
        var friendIds = new HashSet<string>();
        if (userId != null)
        {
            var ids = await friendService.GetFriendIdsAsync(userId);
            friendIds = ids.ToHashSet();
        }

        var withDistance = new List<(PlaceDetailsDto Dto, double Distance)>();

        foreach (var p in candidates)
        {
            if (!IsVisibleToUser(p, userId, friendIds)) continue;

            var dist = DistanceKm(lat, lng, p.Latitude, p.Longitude);
            if (dist > radiusKm) continue;

            var dto = await ToPlaceDetailsDto(p, userId);
            withDistance.Add((dto, dist));
        }

        return withDistance
            .OrderBy(x => x.Distance)
            .Select(x => x.Dto)
            .ToList();
    }

    public async Task<IEnumerable<PlaceDetailsDto>> GetFavoritedPlacesAsync(string userId)
    {
        var favorites = await db.Favorited
            .Where(f => f.UserId == userId)
            // .Where(f => !f.Place.IsDeleted) // ALLOW DELETED PLACES
            .Include(f => f.Place)
                .ThenInclude(p => p.PlaceActivities)
                    .ThenInclude(pa => pa.ActivityKind)
            .AsNoTracking()
            .ToListAsync();

        var list = new List<PlaceDetailsDto>();
        foreach (var f in favorites)
        {
            var p = f.Place;
            var isOwner = p.OwnerUserId == userId;
            bool isVisible = false;

            if (isOwner || p.Visibility == PlaceVisibility.Public)
            {
                isVisible = true;
            }
            else if (p.Visibility == PlaceVisibility.Friends)
            {
                var friendIds = await friendService.GetFriendIdsAsync(userId);
                var isFriend = friendIds.Contains(p.OwnerUserId);
                isVisible = isFriend;
            }

            if (isVisible)
            {
                list.Add(await ToPlaceDetailsDto(p, userId));
            }
        }

        return list;
    }

    // this will soft delete the place by setting IsDeleted to true
    public async Task DeletePlaceAsync(int id, string userId)
    {
       var place = await db.Places.FindAsync(id);
       if (place == null)
       {
           throw new InvalidOperationException("Place not found.");
       }
       
       if (place.OwnerUserId != userId)
       {
           throw new InvalidOperationException("You do not have permission to delete this place.");
       }
       
       place.IsDeleted = true;
       await db.SaveChangesAsync();
    }
    
    public async Task AddFavoriteAsync(int id, string userId)
    {
        var exists = await db.Favorited
            .AnyAsync(f => f.UserId == userId && f.PlaceId == id);
        
        if (exists)
        {
            logger.LogInformation("Place {PlaceId} already favorited by {UserId}", id, userId);
            return;
        }
        
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

        // Increment counter
        var place = await db.Places.FindAsync(id);
        if (place != null)
        {
            place.Favorites++;
        }

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

            // Decrement counter
            var place = await db.Places.FindAsync(id);
            if (place != null && place.Favorites > 0)
            {
                place.Favorites--;
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Place {PlaceId} unfavorited by {UserId}", id, userId);
        }
    }



    private async Task<PlaceDetailsDto> ToPlaceDetailsDto(Place p, string? userId)
    {
        var isOwner = userId != null && p.OwnerUserId == userId;
        
        var isFavorited = userId != null && await db.Favorited
            .AnyAsync(f => f.UserId == userId && f.PlaceId == p.Id);



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
            p.Visibility,
            p.Type,
            isOwner,
            isFavorited,
            p.Favorites,
            activities,
            activityKindNames
        );
    }
}
