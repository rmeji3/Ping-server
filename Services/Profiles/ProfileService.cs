using Ping.Dtos.Profiles;
using Ping.Models.AppUsers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Ping.Data.App;
using Ping.Services.Follows;
using Ping.Models.Follows; // For Follow model if needed
using FriendshipStatus = Ping.Dtos.Profiles.FriendshipStatus; // Alias for DTO enum
using Ping.Services.Storage;
using Ping.Dtos.Activities; // For ActivitySummaryDto
using Ping.Dtos.Reviews; 
using Ping.Dtos.Pings;  
using Ping.Dtos.Events; 
using Ping.Services.Events; 
using Ping.Dtos.Common; 
using Ping.Services.Profiles;
using AppUserPrivacy = Ping.Models.AppUsers.PrivacyConstraint; // Alias for Model enum

using Ping.Services.Blocks;

namespace Ping.Services.Profiles;

public class ProfileService(
    UserManager<AppUser> userManager, 
    ILogger<ProfileService> logger, 
    Ping.Services.Images.IImageService imageService,
    AppDbContext appDb,
    IFollowService followService,
    IBlockService blockService,
    Ping.Services.Moderation.IModerationService moderationService) : IProfileService
{
    public async Task<PersonalProfileDto> GetMyProfileAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            logger.LogWarning("GetMyProfile failed: User {UserId} not found.", userId);
            throw new KeyNotFoundException("User not found.");
        }

        logger.LogDebug("Retrieved profile for {UserName}", user.UserName);

        // Optimizations: Lists are removed from initial load. Fetched via tabs.
        
        var roles = await userManager.GetRolesAsync(user);
        var followersCount = await followService.GetFollowerCountAsync(userId);
        var followingCount = await followService.GetFollowingCountAsync(userId);

        return new PersonalProfileDto(
            user.Id,
            user.UserName!,
            user.ProfileImageUrl,
            user.Bio,
            user.Email!,
            followersCount,
            followingCount,
            roles.ToArray()
        );
    }

    public async Task<PaginatedResult<ProfileDto>> SearchProfilesAsync(string query, string currentUserId, PaginationParams pagination)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Search query is required.");

        var normalized = query.ToUpper(); // match Identity normalization

        var queryable = userManager.Users
            .AsNoTracking()
            .Where(u => u.NormalizedUserName!.StartsWith(normalized)
            && u.Id != currentUserId);

        var blacklisted = await blockService.GetBlacklistedUserIdsAsync(currentUserId);
        if (blacklisted.Count > 0)
        {
            queryable = queryable.Where(u => !blacklisted.Contains(u.Id));
        }

        var totalCount = await queryable.CountAsync();

        var users = await queryable
            .OrderBy(u => u.UserName)
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(u => new ProfileDto(
                u.Id,
                u.UserName!,
                u.ProfileImageUrl,
                u.Bio,
                FriendshipStatus.None, // FriendshipStatus
                0, // ReviewCount
                0, // PingCount
                0, // EventCount
                0, // FollowersCount
                0, // FollowingCount
                false, // IsFriends
                u.ReviewsPrivacy,
                u.PingsPrivacy,
                u.LikesPrivacy
            ))
            .ToListAsync();

        logger.LogDebug("Profile search for '{Query}' returned {Count} results.", query, users.Count);

        return new PaginatedResult<ProfileDto>(users, totalCount, pagination.PageNumber, pagination.PageSize);
    }



    public async Task<string> UpdateProfileImageAsync(string userId, IFormFile file)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            throw new KeyNotFoundException("User not found.");
        }

        // Use ImageService to handle validation, resizing, and uploading
        // We use "profiles" as the folder name
        var (originalUrl, thumbnailUrl) = await imageService.ProcessAndUploadImageAsync(file, "profiles", userId);

        // Update User
        user.ProfileImageUrl = originalUrl;
        user.ProfileThumbnailUrl = thumbnailUrl;
        await userManager.UpdateAsync(user);
        
        logger.LogInformation("Updated profile image for user {UserId}. Original: {Original}, Thumb: {Thumb}", userId, originalUrl, thumbnailUrl);

        return originalUrl;
    }

    public async Task UpdateBioAsync(string userId, string? bio)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) throw new KeyNotFoundException("User not found.");

        if (!string.IsNullOrWhiteSpace(bio))
        {
            var moderation = await moderationService.CheckContentAsync(bio);
            if (moderation.IsFlagged)
            {
                throw new ArgumentException($"Bio contains restricted content: {moderation.Reason}");
            }
        }
        
        user.Bio = bio;
        await userManager.UpdateAsync(user);
    }

    public async Task<ProfileDto> GetProfileByIdAsync(string targetUserId, string currentUserId)
    {
        var user = await userManager.FindByIdAsync(targetUserId);
        if (user is null)
        {
            throw new KeyNotFoundException("User not found.");
        }

        // Check Blocking
        if (targetUserId != currentUserId)
        {
             var isBlocked = await blockService.IsBlockedAsync(currentUserId, targetUserId) || 
                             await blockService.IsBlockedAsync(targetUserId, currentUserId);
             
             if (isBlocked)
             {
                 throw new KeyNotFoundException("User not found.");
             }
        }

        var isSelf = targetUserId == currentUserId;
        
        // Friendship: "Friends" = Mutual Follows.
        var isFollowing = await followService.IsFollowingAsync(currentUserId, targetUserId);
        var isFollowedBy = await followService.IsFollowingAsync(targetUserId, currentUserId);
        var isFriend = isFollowing && isFollowedBy;

        var friendshipStatus = FriendshipStatus.None;
        if (isFriend) friendshipStatus = FriendshipStatus.Accepted;
        else if (isFollowing) friendshipStatus = FriendshipStatus.Following;
        // else: None (even if they follow me, if I don't follow back, no special status in this specific enum for now unless we added 'Follower')

        // Stats
        var reviewCount = await appDb.Reviews.CountAsync(r => r.UserId == targetUserId);
        var eventCount = await appDb.EventAttendees.CountAsync(ea => ea.UserId == targetUserId);
        // "Pings visited" -> distinct pings from reviews + Created Pings
        var pingVisitCount = await appDb.Reviews
            .Where(r => r.UserId == targetUserId)
            .Select(r => r.PingActivity!.PingId)
            .Union(appDb.Pings.Where(p => p.OwnerUserId == targetUserId && !p.IsDeleted).Select(p => p.Id))
            .CountAsync();

        var followersCount = await followService.GetFollowerCountAsync(targetUserId);
        var followingCount = await followService.GetFollowingCountAsync(targetUserId);

        return new ProfileDto(
            user.Id,
            user.UserName!,   // DisplayName (using UserName for now)
            user.ProfileImageUrl,
            user.Bio,
            friendshipStatus,
            reviewCount,
            pingVisitCount,
            eventCount,
            followersCount,
            followingCount,
            isFriend,
            user.ReviewsPrivacy,
            user.PingsPrivacy,
            user.LikesPrivacy
        );
    }

    public async Task<QuickProfileDto> GetQuickProfileAsync(string targetUserId, string currentUserId)
    {
        var user = await userManager.FindByIdAsync(targetUserId);
        if (user is null)
        {
            throw new KeyNotFoundException("User not found.");
        }

        // Check Blocking
        if (targetUserId != currentUserId)
        {
             var isBlocked = await blockService.IsBlockedAsync(currentUserId, targetUserId) || 
                             await blockService.IsBlockedAsync(targetUserId, currentUserId);
             
             if (isBlocked)
             {
                 throw new KeyNotFoundException("User not found.");
             }
        }

        // Friendship
        var isFollowing = await followService.IsFollowingAsync(currentUserId, targetUserId);
        var isFollowedBy = await followService.IsFollowingAsync(targetUserId, currentUserId);
        var isFriend = isFollowing && isFollowedBy;

        var friendshipStatus = FriendshipStatus.None;
        if (isFriend) friendshipStatus = FriendshipStatus.Accepted;
        else if (isFollowing) friendshipStatus = FriendshipStatus.Following;

        // Stats
        var reviewCount = await appDb.Reviews.CountAsync(r => r.UserId == targetUserId);
        var eventCount = await appDb.EventAttendees.CountAsync(ea => ea.UserId == targetUserId);
        // "Pings visited" -> distinct pings from reviews + Created Pings
        var pingVisitCount = await appDb.Reviews
            .Where(r => r.UserId == targetUserId)
            .Select(r => r.PingActivity!.PingId)
            .Union(appDb.Pings.Where(p => p.OwnerUserId == targetUserId && !p.IsDeleted).Select(p => p.Id))
            .CountAsync();

        var followersCount = await followService.GetFollowerCountAsync(targetUserId);
        var followingCount = await followService.GetFollowingCountAsync(targetUserId);

        return new QuickProfileDto(
            user.Id,
            user.UserName!,   
            user.ProfileImageUrl,
            user.Bio,
            friendshipStatus,
            reviewCount,
            pingVisitCount,
            eventCount,
            followersCount,
            followingCount,
            isFriend,
            user.ReviewsPrivacy,
            user.PingsPrivacy,
            user.LikesPrivacy
        );
    }
    public async Task<PaginatedResult<PingDetailsDto>> GetUserPingsAsync(string targetUserId, string currentUserId, PaginationParams pagination)
    {
        var user = await userManager.FindByIdAsync(targetUserId);
        if (user is null) throw new KeyNotFoundException("User not found.");

        if (targetUserId != currentUserId)
        {
             var isBlocked = await blockService.IsBlockedAsync(currentUserId, targetUserId) || 
                             await blockService.IsBlockedAsync(targetUserId, currentUserId);
             if (isBlocked) throw new KeyNotFoundException("User not found.");
        }

        // Check Privacy
        var isFollowing = await followService.IsFollowingAsync(currentUserId, targetUserId);
        var isFollowedBy = await followService.IsFollowingAsync(targetUserId, currentUserId);
        var isFriend = isFollowing && isFollowedBy;
        var isSelf = targetUserId == currentUserId;

        // If not self, check privacy settings
        if (!isSelf)
        {
            bool canViewPings = user.PingsPrivacy == AppUserPrivacy.Public ||
                                 (user.PingsPrivacy == AppUserPrivacy.FriendsOnly && isFriend);
            if (!canViewPings)
            {
                // Return empty if privacy restricts access
                return new PaginatedResult<PingDetailsDto>(new List<PingDetailsDto>(), 0, pagination.PageNumber, pagination.PageSize);
            }
        }

        // Logic: Return {Ping, Date} to allow sorting by recency
        // Created Pings
        var createdQuery = appDb.Pings.AsNoTracking()
            .Where(p => p.OwnerUserId == targetUserId && !p.IsDeleted)
            .Select(p => new { Ping = p, Date = p.CreatedUtc });

        // Visited Pings (from Reviews)
        var visitedQuery = appDb.Reviews.AsNoTracking()
            .Where(r => r.UserId == targetUserId)
            .Select(r => new { Ping = r.PingActivity!.Ping, Date = r.CreatedAt })
            .Where(x => !x.Ping.IsDeleted);

        var combinedQuery = createdQuery.Union(visitedQuery);

        // Apply Visibility Filters to the combined list (to hide private/friends-only pings user shouldn't see)
        // Public: Everyone sees
        // Owner is Me (Viewer): I see
        // Owner is Target (Profile Owner) AND IsFriend: I see (because of 'friends only' visibility logic on the ping + we are friends)
        // Note: Logic copied from GetProfileByIdAsync but adapted for LINQ
        combinedQuery = combinedQuery.Where(x => 
            x.Ping.Visibility == Models.Pings.PingVisibility.Public ||
            x.Ping.OwnerUserId == currentUserId ||
            (x.Ping.Visibility == Models.Pings.PingVisibility.Friends && x.Ping.OwnerUserId == targetUserId && isFriend)
            // Note: We exclude third-party friends-only pings if we aren't the owner, as consistent with GetProfile logic
        );

        var totalCount = await combinedQuery.Select(x => x.Ping.Id).Distinct().CountAsync();
        
        // Paginate - distinct by Ping ID to avoid duplicates if visited + created same ping? 
        // Union handles distinct on the anonymous object {Ping, Date}. 
        // If created and visited at different times, they appear twice? Yes.
        // We probably want unique Pings.
        // GroupBy ID and take latest date.
        
        var pagedItems = await combinedQuery
            .GroupBy(x => x.Ping.Id)
            .Select(g => g.OrderByDescending(x => x.Date).First()) // Take most recent interaction
            .OrderByDescending(x => x.Date)
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(x => x.Ping)
            .ToListAsync();

        var pingDtos = new List<PingDetailsDto>();
        foreach (var p in pagedItems)
        {
            bool isPingOwner = p.OwnerUserId == currentUserId;
            // Map
            pingDtos.Add(new PingDetailsDto(
                p.Id,
                p.Name,
                p.Address ?? string.Empty,
                p.Latitude,
                p.Longitude,
                p.Visibility,
                p.Type,
                isPingOwner,
                false, // IsFavorited - fetching this requires extra query, skipping for list view or need batch check
                0, // Favorites count
                Array.Empty<PingActivitySummaryDto>(),
                p.PingGenre?.Name,
                null,
                p.IsClaimed,
                p.PingGenreId,
                p.PingGenre?.Name,
                p.GooglePlaceId
            ));
        }

        return new PaginatedResult<PingDetailsDto>(pingDtos, totalCount, pagination.PageNumber, pagination.PageSize);
    }

    public async Task<PaginatedResult<EventDto>> GetUserEventsAsync(string targetUserId, string currentUserId, PaginationParams pagination, string? sortBy = null, string? sortOrder = null)
    {
        var user = await userManager.FindByIdAsync(targetUserId);
        if (user is null) throw new KeyNotFoundException("User not found.");

        if (targetUserId != currentUserId)
        {
             var isBlocked = await blockService.IsBlockedAsync(currentUserId, targetUserId) || 
                             await blockService.IsBlockedAsync(targetUserId, currentUserId);
             if (isBlocked) throw new KeyNotFoundException("User not found.");
        }

        var isSelf = targetUserId == currentUserId;
        
        // Created Events
        var createdQuery = appDb.Events.AsNoTracking()
            .Where(e => e.CreatedById == targetUserId);

        // Attending Events
        var attendingQuery = appDb.EventAttendees.AsNoTracking()
            .Where(ea => ea.UserId == targetUserId)
            .Select(ea => ea.Event);

        var combinedQuery = createdQuery.Union(attendingQuery);

        // Visibility Filter
        combinedQuery = combinedQuery.Where(e => 
            e.IsPublic || 
            e.CreatedById == currentUserId || 
            e.Attendees.Any(a => a.UserId == currentUserId)
        );

        var totalCount = await combinedQuery.CountAsync();

        // Default Sort: StartTime Descending
        bool isAscending = sortOrder?.Equals("Asc", StringComparison.OrdinalIgnoreCase) ?? false;
        
        // Currently only "Time" (StartTime) is supported for events sorting as per requirement
        var sortedQuery = isAscending 
            ? combinedQuery.OrderBy(e => e.StartTime) 
            : combinedQuery.OrderByDescending(e => e.StartTime);

        var pagedEvents = await sortedQuery
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Include(e => e.Attendees) // Need attendees for mapping
            .Include(e => e.Ping)
            .ToListAsync();

        var eventDtos = new List<EventDto>();
        if (pagedEvents.Any())
        {
            // Batch fetch creators
            var creatorIds = pagedEvents.Select(e => e.CreatedById).Distinct().ToList();
            var creators = await userManager.Users
                .AsNoTracking()
                .Where(u => creatorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

            // Batch fetch attendees for mapping (only those in the event attendees list)
            // EventMapper needs `attendeeUsers` dictionary to map `Attendees` list.
            var allAttendeeIds = pagedEvents.SelectMany(e => e.Attendees.Select(a => a.UserId)).Distinct().ToList();
            // This could be large. Limit?
            // For card view, we usually need top X attendees or just count. 
            // EventMapper maps ALL attendees.
            var attendeesMap = await userManager.Users
                .AsNoTracking()
                .Where(u => allAttendeeIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

            var friendIds = await followService.GetMutualIdsAsync(currentUserId);

            foreach (var evt in pagedEvents)
            {
                if (creators.TryGetValue(evt.CreatedById, out var creator))
                {
                    var creatorSummary = new UserSummaryDto(creator.Id, creator.UserName!, creator.ProfileImageUrl);
                    eventDtos.Add(EventMapper.MapToDto(evt, creatorSummary, attendeesMap, currentUserId, friendIds));
                }
            }
        }

        return new PaginatedResult<EventDto>(eventDtos, totalCount, pagination.PageNumber, pagination.PageSize);
    }
    public async Task<PaginatedResult<PlaceReviewSummaryDto>> GetProfilePlacesAsync(string targetUserId, string currentUserId, PaginationParams pagination, string? sortBy = null, string? sortOrder = null)
    {
        var user = await userManager.FindByIdAsync(targetUserId);
        if (user is null) throw new KeyNotFoundException("User not found.");

        // Blocking Check (skip if self, but even self shouldn't see if logic is standard, though usually blocks are for *others*)
        if (targetUserId != currentUserId)
        {
            var isBlocked = await blockService.IsBlockedAsync(currentUserId, targetUserId) ||
                            await blockService.IsBlockedAsync(targetUserId, currentUserId);
            if (isBlocked) throw new KeyNotFoundException("User not found.");
        }

        bool isSelf = targetUserId == currentUserId;
        bool isFriend = false; // calculated only if needed

        if (!isSelf)
        {
            // Calculate friendship for privacy checks
            var isFollowing = await followService.IsFollowingAsync(currentUserId, targetUserId);
            var isFollowedBy = await followService.IsFollowingAsync(targetUserId, currentUserId);
            isFriend = isFollowing && isFollowedBy;

            // Check Profile Privacy
            bool canViewReviews = user.ReviewsPrivacy == AppUserPrivacy.Public ||
                                  (user.ReviewsPrivacy == AppUserPrivacy.FriendsOnly && isFriend);
            
            if (!canViewReviews)
            {
                 return new PaginatedResult<PlaceReviewSummaryDto>(new List<PlaceReviewSummaryDto>(), 0, pagination.PageNumber, pagination.PageSize);
            }
        }

        // Fetch Reviews with Pings
        var query = appDb.Reviews.AsNoTracking()
            .Where(r => r.UserId == targetUserId)
            .Include(r => r.PingActivity!)
            .ThenInclude(pa => pa.Ping)
            .Where(r => !r.PingActivity.Ping.IsDeleted);

        // Group by Ping
        // Evaluation: We need to filter PINGS based on privacy too.
        // It's efficient to fetch minimal data first or apply filter in memory if list is small. 
        // Given a user has < 1000 reviews usually, in-memory grouping after fetching minimal fields is okay.
        // But let's try to project needed data to minimize transfer.

        var flatList = await query
            .Select(r => new 
            {
                Ping = r.PingActivity!.Ping,
                Review = new { r.Rating, r.ImageUrl, r.ThumbnailUrl, r.CreatedAt }
            })
            .ToListAsync();

        // In-Memory Grouping & Filtering
        var grouped = flatList
            .GroupBy(x => x.Ping.Id)
            .Select(g => new 
            {
               Ping = g.First().Ping,
               Reviews = g.Select(x => x.Review).ToList(),
               Count = g.Count(),
               AverageRating = g.Average(x => x.Review.Rating),
               LatestReviewDate = g.Max(x => x.Review.CreatedAt)
            });
            
        var validGroups = new List<PlaceReviewSummaryDto>();

        bool isAscending = sortOrder?.Equals("Asc", StringComparison.OrdinalIgnoreCase) ?? false;
        var sortedGroups = string.Equals(sortBy, "Rating", StringComparison.OrdinalIgnoreCase)
            ? (isAscending ? grouped.OrderBy(x => x.AverageRating) : grouped.OrderByDescending(x => x.AverageRating))
            : (isAscending ? grouped.OrderBy(x => x.LatestReviewDate) : grouped.OrderByDescending(x => x.LatestReviewDate));

        foreach (var group in sortedGroups) 
        {
            // Ping Privacy Check
            bool canSeePing = false;

            if (isSelf) 
            {
                canSeePing = true; // I can see all places I visited
            }
            else
            {
                // Visibility Logic:
                // Public: OK
                // Private: Only Owner (Me) - wait, if I visited a private place, I am not the owner usually?
                //          If I am listing places I REVIEWED, and the place is Private, 
                //          it means I reviewed a private place. Who owns it?
                //          If I own it -> I see it (but I am viewer). 
                //          The Viewer is NOT the target user here.
                //          Viewer = CurrentUserId.
                //          Owner = group.Ping.OwnerUserId.
                
                if (group.Ping.Visibility == Models.Pings.PingVisibility.Public)
                {
                    canSeePing = true;
                }
                else if (group.Ping.OwnerUserId == currentUserId)
                {
                    canSeePing = true; // I own this place, so I can see it in their list
                }
                else if (group.Ping.Visibility == Models.Pings.PingVisibility.Friends)
                {
                    // Visible if Viewer is friend of Ping Owner.
                    // Case 1: Ping Owner is Target User (the profile being viewed).
                    if (group.Ping.OwnerUserId == targetUserId)
                    {
                        if (isFriend) canSeePing = true;
                    }
                    // Case 2: Ping Owner is Third Party.
                    // Optimization: Skip checking third-party friendship for now to avoid complexity/N+1. 
                    // Assume hidden unless Public or Owned by Viewer or Owned by Target(Friend).
                }
            }

            if (canSeePing)
            {
                // Thumbnails: Top 3 most recent with images
                var thumbnails = group.Reviews
                 // Using 'Review' anonymous object from above projection.
                 // Note: 'Review' in `flatList` doesn't have `Review` property, it IS the object.
                 // Correction: group.Reviews is List of { Rating, ImageUrl, ThumbnailUrl, CreatedAt }
                    .Where(r => !string.IsNullOrEmpty(r.ThumbnailUrl))
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(3)
                    .Select(r => r.ThumbnailUrl!)
                    .ToList();

                validGroups.Add(new PlaceReviewSummaryDto(
                    group.Ping.Id,
                    group.Ping.Name,
                    group.Ping.Address ?? "",
                    Math.Round(group.AverageRating, 1),
                    group.Count,
                    thumbnails
                ));
            }
        }

        // Pagination on the resulting list
        var totalCount = validGroups.Count;
        var pagedItems = validGroups
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToList();

        return new PaginatedResult<PlaceReviewSummaryDto>(pagedItems, totalCount, pagination.PageNumber, pagination.PageSize);
    }

    public async Task<PaginatedResult<ReviewDto>> GetProfilePlaceReviewsAsync(string targetUserId, int pingId, string currentUserId, PaginationParams pagination)
    {
        var user = await userManager.FindByIdAsync(targetUserId);
        if (user is null) throw new KeyNotFoundException("User not found.");

        if (targetUserId != currentUserId)
        {
            var isBlocked = await blockService.IsBlockedAsync(currentUserId, targetUserId) ||
                            await blockService.IsBlockedAsync(targetUserId, currentUserId);
            if (isBlocked) throw new KeyNotFoundException("User not found.");
        }

        bool isSelf = targetUserId == currentUserId;
        bool isFriend = false;

        // 1. Profile Privacy Check
        if (!isSelf)
        {
            var isFollowing = await followService.IsFollowingAsync(currentUserId, targetUserId);
            var isFollowedBy = await followService.IsFollowingAsync(targetUserId, currentUserId);
            isFriend = isFollowing && isFollowedBy;

            bool canViewReviews = user.ReviewsPrivacy == AppUserPrivacy.Public ||
                                  (user.ReviewsPrivacy == AppUserPrivacy.FriendsOnly && isFriend);
            
            if (!canViewReviews) throw new KeyNotFoundException("Reviews not found or private.");
        }

        // 2. Ping Privacy Check
        // We need the ping to check visibility
        var ping = await appDb.Pings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pingId);
        if (ping == null || ping.IsDeleted) throw new KeyNotFoundException("Place not found.");

        if (!isSelf)
        {
             bool canSeePing = false;
             if (ping.Visibility == Models.Pings.PingVisibility.Public) canSeePing = true;
             else if (ping.OwnerUserId == currentUserId) canSeePing = true;
             else if (ping.Visibility == Models.Pings.PingVisibility.Friends && ping.OwnerUserId == targetUserId && isFriend) canSeePing = true;
             
             if (!canSeePing) throw new KeyNotFoundException("Place not found or private.");
        }

        // Fetch Reviews
        var query = appDb.Reviews.AsNoTracking()
            .Where(r => r.UserId == targetUserId && r.PingActivity!.PingId == pingId)
            .Include(r => r.ReviewTags).ThenInclude(rt => rt.Tag)
            .OrderByDescending(r => r.CreatedAt); // Newest first

        var totalCount = await query.CountAsync();
        
        var reviews = await query
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(r => new ReviewDto(
                r.Id,
                r.Rating,
                r.Content,
                r.UserId,
                user.UserName!,
                user.ProfileImageUrl,
                r.ImageUrl,
                r.ThumbnailUrl,
                r.CreatedAt,
                r.LikesList.Count,
                r.LikesList.Any(l => l.UserId == currentUserId),
                r.ReviewTags.Select(rt => rt.Tag.Name).ToList()
            ))
            .ToListAsync();

        return new PaginatedResult<ReviewDto>(reviews, totalCount, pagination.PageNumber, pagination.PageSize);
    }

    public async Task UpdateProfilePrivacyAsync(string userId, PrivacySettingsDto settings)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) throw new KeyNotFoundException("User not found.");

        bool changed = false;

        if (settings.ReviewsPrivacy.HasValue)
        {
            user.ReviewsPrivacy = settings.ReviewsPrivacy.Value;
            changed = true;
        }

        if (settings.PingsPrivacy.HasValue)
        {
            user.PingsPrivacy = settings.PingsPrivacy.Value;
            changed = true;
        }

        if (settings.LikesPrivacy.HasValue)
        {
            user.LikesPrivacy = settings.LikesPrivacy.Value;
            changed = true;
        }

        if (changed)
        {
            await userManager.UpdateAsync(user);
        }
    }
}

