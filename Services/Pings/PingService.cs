using Ping.Data.App;
using Ping.Dtos.Activities;
using Ping.Dtos.Common;
using Ping.Dtos.Pings;
using Ping.Models.Pings;
using Ping.Models.Business;
using Ping.Utils;
using Ping.Models.Reviews;
using Ping.Services.Follows;
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
    IFollowService followService,
    Services.Moderation.IModerationService moderationService,
    Services.AI.ISemanticService semanticService,
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
                if (!string.IsNullOrWhiteSpace(dto.GooglePlaceId))
                {
                    // New "Verified via ID" path
                    logger.LogInformation("Verified ping with GooglePlaceId {Id}. Verifying name match...", dto.GooglePlaceId);
                    
                    var googlePlace = await pingNameService.GetGooglePlaceByIdAsync(dto.GooglePlaceId);
                    if (googlePlace != null)
                    {
                        var isMatch = await semanticService.VerifyPlaceNameMatchAsync(googlePlace.Name, finalName);
                        if (isMatch)
                        {
                            logger.LogInformation("AI Verified: '{User}' matches '{Official}'. Keeping Verified status.", finalName, googlePlace.Name);
                            // Keep PingType.Verified
                            // We use the USER provided name (finalName) as requested
                            // Ensure lat/lng are accurate if available from Google
                            if (googlePlace.Lat.HasValue && googlePlace.Lng.HasValue)
                            {
                                dto = dto with { Latitude = googlePlace.Lat.Value, Longitude = googlePlace.Lng.Value };
                            }
                            
                            // DUPLICATE CHECK: Verify if this GooglePlaceId already exists in our DB
                            var existingByPlaceId = await db.Pings
                                .Where(p => p.GooglePlaceId == dto.GooglePlaceId && !p.IsDeleted)
                                .FirstOrDefaultAsync();

                            if (existingByPlaceId != null)
                            {
                                logger.LogInformation("Verified ping already exists with GooglePlaceId '{PlaceId}'. Returning error.", dto.GooglePlaceId);
                                throw new InvalidOperationException($"Ping already exists: {existingByPlaceId.Name}");
                            }
                        }
                        else
                        {
                            logger.LogWarning("AI Verification Failed: '{User}' does NOT match '{Official}'. Downgrading to Custom.", finalName, googlePlace.Name);
                            dto = dto with { Type = PingType.Custom };
                        }
                    }
                    else
                    {
                        logger.LogError("GooglePlaceId {Id} provided but not found. Downgrading to Custom.", dto.GooglePlaceId);
                        dto = dto with { Type = PingType.Custom };
                    }
                }
                else
                {
                    // Legacy "Verified via Address/Location" path
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
                        logger.LogInformation("Verified ping already exists with address '{Address}'. Returning error.", dto.Address);
                        throw new InvalidOperationException($"Ping already exists: {existingByAddress.Name}");
                    }

                    // Fetch Google name for verified pings (Overwrite behavior)
                    logger.LogInformation("Verified ping detected (Legacy). Calling Google Places API for coordinates {Lat}, {Lng}", dto.Latitude, dto.Longitude);
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
            }
            else // PingType.Custom
            {
                // Check for duplicates using AI (Semantic check against ALL public pings nearby)
                var newPoint = new Point(dto.Longitude, dto.Latitude) { SRID = 4326 };
                var nearbyPings = await db.Pings
                    .Where(p => p.Visibility == PingVisibility.Public &&
                                p.Location.IsWithinDistance(newPoint, 0.0005)) // ~50m radius
                    .ToListAsync();
                
                Models.Pings.Ping? existingMatch = null;

                if (nearbyPings.Any())
                {
                    // 1. Exact Name Match (Case-Insensitive)
                    existingMatch = nearbyPings.FirstOrDefault(p => p.Name.Equals(finalName, StringComparison.OrdinalIgnoreCase));
                    
                    // 2. AI Semantic Match if no exact match
                    if (existingMatch == null)
                    {
                        var duplicateName = await semanticService.FindDuplicateAsync(finalName, nearbyPings.Select(p => p.Name));
                        if (duplicateName != null)
                        {
                            existingMatch = nearbyPings.FirstOrDefault(p => p.Name == duplicateName);
                        }
                    }
                }

                if (existingMatch != null)
                {
                    logger.LogInformation("Ping duplicate detected: '{NewName}' matches existing '{ExistingName}' ({Id}). Returning error.", finalName, existingMatch.Name, existingMatch.Id);
                    throw new InvalidOperationException($"Ping already exists: {existingMatch.Name}");
                }

                logger.LogInformation("Custom ping verified unique: '{UserName}'", finalName);
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
            GooglePlaceId = dto.GooglePlaceId,
            CreatedUtc = DateTime.UtcNow
        };

        db.Pings.Add(ping);
        await db.SaveChangesAsync();

        logger.LogInformation("Ping created: {PingId} by {UserId}. Visibility: {Visibility}. Daily count: {Count}/{Limit}", 
            ping.Id, userId, dto.Visibility, createdToday, limit);

        return await ToPingDetailsDto(ping, userId);
    }

    public async Task<PingDetailsDto> UpdatePingAsync(int id, UpdatePingDto dto, string userId)
    {
        var ping = await db.Pings.FindAsync(id);
        if (ping == null)
            throw new InvalidOperationException("Ping not found.");

        if (ping.OwnerUserId != userId)
            throw new InvalidOperationException("You do not have permission to update this ping.");

        var targetName = !string.IsNullOrWhiteSpace(dto.Name) ? dto.Name.Trim() : ping.Name;

        if (dto.Name != null && ping.Name != targetName)
        {
            if (ping.Type == PingType.Custom)
            {
                 var mod = await moderationService.CheckContentAsync(targetName);
                 if (mod.IsFlagged)
                 {
                     logger.LogWarning("Ping name flagged on update: {Name} - {Reason}", targetName, mod.Reason);
                     throw new ArgumentException($"Ping name rejected: {mod.Reason}");
                 }
            }
            
            // If the ping is Verified, we must ensure the new name still matches the official Google Place name
            if (ping.Type == PingType.Verified && !string.IsNullOrWhiteSpace(ping.GooglePlaceId))
            {
                 var googlePlace = await pingNameService.GetGooglePlaceByIdAsync(ping.GooglePlaceId);
                 if (googlePlace != null)
                 {
                     if (!await semanticService.VerifyPlaceNameMatchAsync(googlePlace.Name, targetName))
                     {
                          logger.LogWarning("Update Ping: Name '{User}' does NOT match '{Official}'. Force downgrading to Custom.", targetName, googlePlace.Name);
                          ping.Type = PingType.Custom;
                     }
                 }
            }

            ping.Name = targetName;
        }

        if (dto.PingGenreId.HasValue) ping.PingGenreId = dto.PingGenreId;

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
                var friendIds = await followService.GetMutualIdsAsync(userId);
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


    public async Task<PaginatedResult<PingDetailsDto>> SearchPingsAsync(PingSearchFilterDto filter, string? userId)
    {
        logger.LogDebug("Ping search: lat={Lat}, lng={Lng}, query={Query}, tags={Tags}", 
            filter.Latitude, filter.Longitude, filter.Query, filter.Tags != null ? string.Join(",", filter.Tags) : "none");

        var q = db.Pings
            .Where(p => !p.IsDeleted)
            .Include(p => p.PingActivities)
            .Include(p => p.PingGenre)
            .AsNoTracking();

        // Keyword Search
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var search = filter.Query.Trim().ToLowerInvariant();
            q = q.Where(p => p.Name.ToLower().Contains(search) || (p.Address != null && p.Address.ToLower().Contains(search)));
        }

        // Tags Search
        if (filter.Tags != null && filter.Tags.Any())
        {
            var normalizedTags = filter.Tags.Select(t => t.Trim().ToLowerInvariant()).ToList();
            q = q.Where(p => p.PingActivities
                .Any(a => a.Reviews
                    .Any(r => r.ReviewTags.Any(rt => normalizedTags.Contains(rt.Tag.Name.ToLower())))));
        }

        // Geospatial
        if (filter.Latitude.HasValue && filter.Longitude.HasValue && filter.RadiusKm.HasValue)
        {
            var searchPoint = new Point(filter.Longitude.Value, filter.Latitude.Value) { SRID = 4326 };
            double radiusDegrees = filter.RadiusKm.Value / 111.32;
            q = q.Where(p => p.Location.IsWithinDistance(searchPoint, radiusDegrees));
        }

        if (filter.Visibility.HasValue)
        {
            q = q.Where(p => p.Visibility == filter.Visibility.Value);
        }

        if (filter.Type.HasValue)
        {
            q = q.Where(p => p.Type == filter.Type.Value);
        }

        if (filter.ActivityNames != null && filter.ActivityNames.Any())
        {
            var normalizedNames = filter.ActivityNames.Select(a => a.Trim().ToLowerInvariant()).ToList();
            q = q.Where(p => p.PingActivities.Any(a => a.Name != null && normalizedNames.Contains(a.Name.ToLower())));
        }

        if (filter.PingGenreNames != null && filter.PingGenreNames.Any())
        {
            var normalizedGenres = filter.PingGenreNames.Select(g => g.Trim().ToLowerInvariant()).ToList();
            q = q.Where(p => p.PingGenre != null && normalizedGenres.Contains(p.PingGenre.Name.ToLower()));
        }

        var candidates = await q.ToListAsync();

        var friendIds = new HashSet<string>();
        if (userId != null)
        {
            var ids = await followService.GetMutualIdsAsync(userId);
            friendIds = ids.ToHashSet();
        }

        var mapped = new List<(PingDetailsDto Dto, double? Distance)>();

        foreach (var p in candidates)
        {
            if (!IsVisibleToUser(p, userId, friendIds)) continue;
            
            double? dist = null;
            if (filter.Latitude.HasValue && filter.Longitude.HasValue) 
            {
                dist = DistanceKm(filter.Latitude.Value, filter.Longitude.Value, p.Latitude, p.Longitude);
            }
            
            var dto = await ToPingDetailsDto(p, userId);
            mapped.Add((dto, dist));
        }

        var sorted = mapped
            .OrderBy(x => x.Distance ?? double.MaxValue)
            .ThenBy(x => x.Dto.Name)
            .Select(x => x.Dto);

        var pagination = new PaginationParams { PageNumber = filter.PageNumber, PageSize = filter.PageSize };
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
                var friendIds = await followService.GetMutualIdsAsync(userId);
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
            // Ensure consistency with "All" collection even if Favorited table has it
            await EnsurePingInAllCollectionAsync(id, userId);
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

        await EnsurePingInAllCollectionAsync(id, userId);

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

            // Also remove from "All" collection
            var allCollection = await db.Collections
                .FirstOrDefaultAsync(c => c.UserId == userId && c.Name == "All");
            
            if (allCollection != null)
            {
                var cp = await db.CollectionPings
                    .FirstOrDefaultAsync(x => x.CollectionId == allCollection.Id && x.PingId == id);
                
                if (cp != null)
                {
                    db.CollectionPings.Remove(cp);
                }
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Ping {PingId} unfavorited by {UserId}", id, userId);
        }
    }

    private async Task EnsurePingInAllCollectionAsync(int pingId, string userId)
    {
        // 1. Get or Create "All" collection
        var allCollection = await db.Collections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Name == "All");

        if (allCollection == null)
        {
            allCollection = new Collection
            {
                UserId = userId,
                Name = "All",
                IsPublic = false, // Private by default
                CreatedUtc = DateTime.UtcNow
            };
            db.Collections.Add(allCollection);
            await db.SaveChangesAsync(); // Save to get Id
        }

        // 2. Add Ping to Collection if not present
        var exists = await db.CollectionPings
            .AnyAsync(cp => cp.CollectionId == allCollection.Id && cp.PingId == pingId);

        if (!exists)
        {
            db.CollectionPings.Add(new CollectionPing 
            { 
                CollectionId = allCollection.Id, 
                PingId = pingId,
                AddedUtc = DateTime.UtcNow
            });
            // We do not call SaveChangesAsync here to allow batching with parent method, 
            // but parent method calls SaveChangesAsync at the end.
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
            p.PingGenre?.Name,
            (ClaimStatus?)claim?.Status,
            p.IsClaimed,
            p.PingGenreId,
            p.PingGenre?.Name,
            p.GooglePlaceId
        );
    }
}

