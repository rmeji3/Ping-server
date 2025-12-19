using Ping.Data.App;
using Ping.Dtos.Activities;
using Ping.Dtos.Common;
using Ping.Dtos.Pings;
using Ping.Models.Pings;
using Ping.Models.Business;
using Ping.Utils;
using Ping.Models.Reviews;
using Ping.Services.Friends;
using Ping.Services.Google;
using Ping.Services.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace Ping.Services.Pings;

public class PingService(
    AppDbContext db,
    IRedisService redis,
    IConfiguration config,
    IPingNameService pingNameService,
    IFriendService friendService,
    Services.Moderation.IModerationService moderationService,
    ILogger<PingService> logger) : IPingService
{
    public async Task<PingDetailsDto> CreatePingAsync(UpsertPingDto dto, string userId)
    {
        // Redis-based daily rate limit per user
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var rateLimitKey = $"ratelimit:ping:create:{userId}:{today}";
        var limit = config.GetValue<int>("RateLimiting:PingCreationLimitPerDay", 10);

        // Increment counter with 24-hour expiry
        var createdToday = await redis.IncrementAsync(rateLimitKey, TimeSpan.FromHours(24));

        if (createdToday > limit)
        {
            logger.LogWarning("Ping creation rate limit reached for user {UserId}. Count: {Count}", userId, createdToday);
            throw new InvalidOperationException("You've reached the daily limit for adding pings.");
        }

        // RULE: Private/Friends pings can only be Custom (not Verified)
        if (dto.Visibility != PingVisibility.Public && dto.Type == PingType.Verified)
        {
            logger.LogWarning("Verified type is only allowed for Public pings. Auto-correcting to Custom for {Visibility} ping.", dto.Visibility);
            dto = dto with { Type = PingType.Custom };
        }

        var finalName = dto.Name.Trim();

        if (dto.Type == PingType.Custom)
        {
             var mod = await moderationService.CheckContentAsync(finalName);
             if (mod.IsFlagged)
             {
                 logger.LogWarning("Ping name flagged: {Name} - {Reason}", finalName, mod.Reason);
                 throw new ArgumentException($"Ping name rejected: {mod.Reason}");
             }
        }

        if (dto.Visibility == PingVisibility.Public)
        {
            if (dto.Type == PingType.Verified)
            {
                if (string.IsNullOrWhiteSpace(dto.Address))
                {
                    logger.LogError("Verified ping creation failed: Address is required for verified pings.");
                    throw new InvalidOperationException("Verified pings require an address.");
                }

                var existingByAddress = await db.Pings
                    .Where(p => p.Visibility == PingVisibility.Public &&
                                p.Type == PingType.Verified &&
                                p.Address == dto.Address.Trim())
                    .FirstOrDefaultAsync();

                if (existingByAddress != null)
                {
                    logger.LogInformation("Verified ping already exists with address '{Address}'. Returning existing ping {PingId}.", dto.Address, existingByAddress.Id);
                    return await ToPingDetailsDto(existingByAddress, userId);
                }

                // Fetch Google name for verified pings
                logger.LogInformation("Verified ping detected. Calling Google Places API for coordinates {Lat}, {Lng}", dto.Latitude, dto.Longitude);
                var googleName = await pingNameService.GetPingNameAsync(dto.Latitude, dto.Longitude);
                
                if (!string.IsNullOrWhiteSpace(googleName))
                {
                    logger.LogInformation("Using Google Places name: '{GoogleName}' for verified ping.", googleName);
                    finalName = googleName;
                }
                else
                {
                    logger.LogInformation("Google Places returned no name. Using user-provided name: '{UserName}'", finalName);
                }
            }
            else // PingType.Custom
            {
                var newPoint = new Point(dto.Longitude, dto.Latitude) { SRID = 4326 };
                var existingCustom = await db.Pings
                    .Where(p => p.Visibility == PingVisibility.Public &&
                                p.Type == PingType.Custom &&
                                p.Location.IsWithinDistance(newPoint, 0.0005))
                    .FirstOrDefaultAsync();

                if (existingCustom != null)
                {
                    logger.LogInformation("Custom ping already exists within 50m at coordinates {Lat}, {Lng}. Returning existing ping {PingId}.", dto.Latitude, dto.Longitude, existingCustom.Id);
                    return await ToPingDetailsDto(existingCustom, userId);
                }

                logger.LogInformation("Custom ping created with user-provided name: '{UserName}'", finalName);
            }
        }
        else
        {
            logger.LogInformation("Non-public ping (Visibility: {Visibility}). Skipping duplicate checks. Using user name: '{UserName}'", dto.Visibility, finalName);
        }

        var ping = new Models.Pings.Ping
        {
            Name = finalName,
            Address = dto.Address?.Trim() ?? string.Empty,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            OwnerUserId = userId,
            Visibility = dto.Visibility,
            Type = dto.Type,
            PingGenreId = dto.PingGenreId,
            CreatedUtc = DateTime.UtcNow
        };

        db.Pings.Add(ping);
        await db.SaveChangesAsync();

        logger.LogInformation("Ping created: {PingId} by {UserId}. Visibility: {Visibility}. Daily count: {Count}/{Limit}", 
            ping.Id, userId, dto.Visibility, createdToday, limit);

        return await ToPingDetailsDto(ping, userId);
    }

    public async Task<PingDetailsDto> UpdatePingAsync(int id, UpsertPingDto dto, string userId)
    {
        var ping = await db.Pings.FindAsync(id);
        if (ping == null)
            throw new InvalidOperationException("Ping not found.");

        if (ping.OwnerUserId != userId)
            throw new InvalidOperationException("You do not have permission to update this ping.");

        if (dto.Visibility != PingVisibility.Public && dto.Type == PingType.Verified)
        {
             logger.LogWarning("Verified type is only allowed for Public pings. Auto-correcting to Custom for {Visibility} ping update.", dto.Visibility);
             dto = dto with { Type = PingType.Custom };
        }

        var finalName = dto.Name.Trim();

        if (ping.Name != finalName)
        {
            if (dto.Type == PingType.Custom)
            {
                 var mod = await moderationService.CheckContentAsync(finalName);
                 if (mod.IsFlagged)
                 {
                     logger.LogWarning("Ping name flagged on update: {Name} - {Reason}", finalName, mod.Reason);
                     throw new ArgumentException($"Ping name rejected: {mod.Reason}");
                 }
            }
        }

        ping.Name = finalName;
        ping.Address = dto.Address?.Trim() ?? string.Empty;
        ping.Latitude = dto.Latitude;
        ping.Longitude = dto.Longitude;
        ping.Visibility = dto.Visibility;
        ping.Type = dto.Type;
        ping.PingGenreId = dto.PingGenreId;

        await db.SaveChangesAsync();

        logger.LogInformation("Ping {PingId} updated by {UserId}.", ping.Id, userId);

        return await ToPingDetailsDto(ping, userId);
    }

    public async Task<PingDetailsDto?> GetPingByIdAsync(int id, string? userId)
    {
        var p = await db.Pings
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.PingActivities)
            .Include(x => x.PingGenre)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p is null) return null;

        var isOwner = userId != null && p.OwnerUserId == userId;

        if (!isOwner)
        {
            if (p.Visibility == PingVisibility.Private)
                return null;

            if (p.Visibility == PingVisibility.Friends && userId != null)
            {
                var friendIds = await friendService.GetFriendIdsAsync(userId);
                var isFriend = friendIds.Contains(p.OwnerUserId);
                if (!isFriend) return null;
            }
            else if (p.Visibility == PingVisibility.Friends && userId == null)
            {
                return null;
            }
        }

        return await ToPingDetailsDto(p, userId);
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
    private static bool IsVisibleToUser(Models.Pings.Ping p, string? userId, HashSet<string> friendIds)
    {
        var isOwner = userId != null && p.OwnerUserId == userId;

        if (isOwner) return true;

        return p.Visibility switch
        {
            PingVisibility.Public  => true,
            PingVisibility.Private => false,
            PingVisibility.Friends => userId != null && friendIds.Contains(p.OwnerUserId),
            _ => false
        };
    }


    public async Task<PaginatedResult<PingDetailsDto>> SearchNearbyAsync(
        double lat,
        double lng,
        double radiusKm,
        string? activityName,
        string? pingGenreName, // was activityKind, now filtering by PingGenre? Or Activity Name still?
        PingVisibility? visibility,
        PingType? type,
        string? userId,
        PaginationParams pagination
    )
    {
        var searchPoint = new Point(lng, lat) { SRID = 4326 };
        double radiusDegrees = radiusKm / 111.32;

        logger.LogDebug("Nearby search: lat={Lat}, lng={Lng}, radius={Radius}km ({Deg} deg), vis={Vis}, type={Type}", lat, lng, radiusKm, radiusDegrees, visibility, type);

        var q = db.Pings
        .Where(p => !p.IsDeleted)
        .Include(p => p.PingActivities)
        .Include(p => p.PingGenre)
        .AsNoTracking();

        q = q.Where(p => p.Location.IsWithinDistance(searchPoint, radiusDegrees));

        if (visibility.HasValue)
        {
            q = q.Where(p => p.Visibility == visibility.Value);
        }

        if (type.HasValue)
        {
            q = q.Where(p => p.Type == type.Value);
        }

        if (!string.IsNullOrWhiteSpace(activityName))
        {
            var an = activityName.Trim().ToLowerInvariant();
            q = q.Where(p => p.PingActivities.Any(a => a.Name != null && a.Name.ToLower() == an));
        }

        if (!string.IsNullOrWhiteSpace(pingGenreName))
        {
            var gn = pingGenreName.Trim().ToLowerInvariant();
            q = q.Where(p => p.PingGenre != null && p.PingGenre.Name.ToLower() == gn);
        }

        var candidates = await q.ToListAsync();

        var friendIds = new HashSet<string>();
        if (userId != null)
        {
            var ids = await friendService.GetFriendIdsAsync(userId);
            friendIds = ids.ToHashSet();
        }

        var withDistance = new List<(PingDetailsDto Dto, double Distance)>();

        foreach (var p in candidates)
        {
            if (!IsVisibleToUser(p, userId, friendIds)) continue;
            
            var dist = DistanceKm(lat, lng, p.Latitude, p.Longitude);
            
            var dto = await ToPingDetailsDto(p, userId);
            withDistance.Add((dto, dist));
        }

        var sorted = withDistance
            .OrderBy(x => x.Distance)
            .Select(x => x.Dto);

        return sorted.ToPaginatedResult(pagination);
    }

    public async Task<PaginatedResult<PingDetailsDto>> GetFavoritedPingsAsync(string userId, PaginationParams pagination)
    {
        var favorites = await db.Favorited
            .Where(f => f.UserId == userId)
            .Include(f => f.Ping)
                .ThenInclude(p => p.PingActivities)
            .Include(f => f.Ping)
                .ThenInclude(p => p.PingGenre)
            .AsNoTracking()
            .ToListAsync();

        var list = new List<PingDetailsDto>();
        foreach (var f in favorites)
        {
            var p = f.Ping;
            var isOwner = p.OwnerUserId == userId;
            bool isVisible = false;

            if (isOwner || p.Visibility == PingVisibility.Public)
            {
                isVisible = true;
            }
            else if (p.Visibility == PingVisibility.Friends)
            {
                var friendIds = await friendService.GetFriendIdsAsync(userId);
                var isFriend = friendIds.Contains(p.OwnerUserId);
                isVisible = isFriend;
            }

            if (isVisible)
            {
                list.Add(await ToPingDetailsDto(p, userId));
            }
        }

        return list.ToPaginatedResult(pagination);
    }

    public async Task DeletePingAsync(int id, string userId)
    {
       var ping = await db.Pings.FindAsync(id);
       if (ping == null)
       {
           throw new InvalidOperationException("Ping not found.");
       }
       
       if (ping.OwnerUserId != userId)
       {
           throw new InvalidOperationException("You do not have permission to delete this ping.");
       }
       
       ping.IsDeleted = true;
       await db.SaveChangesAsync();
    }

    public async Task DeletePingAsAdminAsync(int id)
    {
        var ping = await db.Pings.FindAsync(id);
        if (ping == null) throw new KeyNotFoundException("Ping not found");
        
        ping.IsDeleted = true;
        await db.SaveChangesAsync();
        logger.LogInformation("Ping soft-deleted by admin: {PingId}", id);
    }
    
    public async Task AddFavoriteAsync(int id, string userId)
    {
        var exists = await db.Favorited
            .AnyAsync(f => f.UserId == userId && f.PingId == id);
        
        if (exists)
        {
            logger.LogInformation("Ping {PingId} already favorited by {UserId}", id, userId);
            return;
        }
        
        var pingExists = await db.Pings.AnyAsync(p => p.Id == id);
        if (!pingExists)
        {
            throw new InvalidOperationException("Ping not found.");
        }
        
        var favorited = new Favorited
        {
            UserId = userId,
            PingId = id
        };
        await db.Favorited.AddAsync(favorited);

        var ping = await db.Pings.FindAsync(id);
        if (ping != null)
        {
            ping.Favorites++;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Ping {PingId} favorited by {UserId}", id, userId);
    }

    public async Task UnfavoriteAsync(int id, string userId)
    {
        var favorited = await db.Favorited
            .FirstOrDefaultAsync(f => f.UserId == userId && f.PingId == id);
        
        if (favorited != null)
        {
            db.Favorited.Remove(favorited);

            var ping = await db.Pings.FindAsync(id);
            if (ping != null && ping.Favorites > 0)
            {
                ping.Favorites--;
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Ping {PingId} unfavorited by {UserId}", id, userId);
        }
    }

    public async Task<List<PingDetailsDto>> GetPingsByOwnerAsync(string userId, bool onlyClaimed = false)
    {
        var q = db.Pings
            .Where(p => p.OwnerUserId == userId && !p.IsDeleted);

        if (onlyClaimed)
        {
            q = q.Where(p => p.IsClaimed);
        }

        var pings = await q
            .Include(p => p.PingActivities)
            .Include(p => p.PingGenre)
            .AsNoTracking()
            .ToListAsync();

        var list = new List<PingDetailsDto>();
        foreach (var p in pings)
        {
            list.Add(await ToPingDetailsDto(p, userId));
        }
        return list;
    }

    private async Task<PingDetailsDto> ToPingDetailsDto(Models.Pings.Ping p, string? userId)
    {
        var isOwner = userId != null && p.OwnerUserId == userId;
        
        var isFavorited = userId != null && await db.Favorited
            .AnyAsync(f => f.UserId == userId && f.PingId == p.Id);

        // Ping Claim logic
        // Assuming PlaceClaims renamed to PingClaims in DbContext
        var claim = userId != null 
            ? await db.PingClaims.FirstOrDefaultAsync(c => c.PingId == p.Id && c.UserId == userId) 
            : null;

        var activities = p.PingActivities
            .Select(a => new PingActivitySummaryDto(
                a.Id,
                a.Name,
                null, // No more ActivityKindId
                null  // No more ActivityKindName per activity
            ))
            .ToArray();

        // For PingGenres, assume single genre
        string[] pingGenres = p.PingGenre != null ? [p.PingGenre.Name] : [];

        return new PingDetailsDto(
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
            pingGenres,
            (ClaimStatus?)claim?.Status,
            p.IsClaimed,
            p.PingGenreId,
            p.PingGenre?.Name
        );
    }
}

