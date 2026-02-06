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
        
        var reviewCount = await appDb.Reviews.CountAsync(r => r.UserId == userId && !r.PingActivity!.Ping.IsDeleted);
        var eventCount = await appDb.EventAttendees.CountAsync(ea => ea.UserId == userId);
        var pingVisitCount = await appDb.Reviews
            .Where(r => r.UserId == userId && !r.PingActivity!.Ping.IsDeleted)
            .Select(r => r.PingActivity!.PingId)
            .Union(appDb.Pings.Where(p => p.OwnerUserId == userId && !p.IsDeleted).Select(p => p.Id))
            .CountAsync();

        return new PersonalProfileDto(
            user.Id,
            user.UserName!,
            user.ProfileImageUrl,
            user.Bio,
            user.Email!,
            reviewCount,
            pingVisitCount,
            eventCount,
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
            .ToListAsync();

        var userIds = users.Select(u => u.Id).ToList();

        // Batch Fetch Counts
        var followerCounts = await followService.GetFollowerCountsAsync(userIds);
        var followingCounts = await followService.GetFollowingCountsAsync(userIds);
        var friendshipStatuses = await followService.GetFriendshipStatusesAsync(currentUserId, userIds);

        // Review Counts (Active Pings only)
        var reviewCounts = await appDb.Reviews.AsNoTracking()
            .Where(r => userIds.Contains(r.UserId) && !r.PingActivity!.Ping.IsDeleted)
            .GroupBy(r => r.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        // Event Counts
        var eventCounts = await appDb.EventAttendees.AsNoTracking()
            .Where(ea => userIds.Contains(ea.UserId))
            .GroupBy(ea => ea.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        // Ping Counts (Created + Visited, Active)
        var validPingIdsCreated = await appDb.Pings.AsNoTracking()
             .Where(p => userIds.Contains(p.OwnerUserId) && !p.IsDeleted)
             .Select(p => new { p.OwnerUserId, p.Id })
             .ToListAsync();

        var visitedPings = await appDb.Reviews.AsNoTracking()
             .Where(r => userIds.Contains(r.UserId) && !r.PingActivity!.Ping.IsDeleted)
             .Select(r => new { r.UserId, PingId = r.PingActivity.PingId })
             .Distinct()
             .ToListAsync();

        var pingCounts = new Dictionary<string, int>();
        foreach (var uid in userIds)
        {
            var created = validPingIdsCreated.Where(x => x.OwnerUserId == uid).Select(x => x.Id);
            var visited = visitedPings.Where(x => x.UserId == uid).Select(x => x.PingId);
            pingCounts[uid] = created.Union(visited).Distinct().Count();
        }

        var profileDtos = users.Select(u => {
             var status = friendshipStatuses.GetValueOrDefault(u.Id, FriendshipStatus.None);
             var isFriend = status == FriendshipStatus.Accepted;

             return new ProfileDto(
                u.Id,
                u.UserName!, // DisplayName
                u.ProfileImageUrl,
                u.Bio,
                status,
                reviewCounts.GetValueOrDefault(u.Id, 0),
                pingCounts.GetValueOrDefault(u.Id, 0),
                eventCounts.GetValueOrDefault(u.Id, 0),
                followerCounts.GetValueOrDefault(u.Id, 0),
                followingCounts.GetValueOrDefault(u.Id, 0),
                isFriend,
                u.ReviewsPrivacy,
                u.PingsPrivacy,
                u.LikesPrivacy
             );
        }).ToList();

        logger.LogDebug("Profile search for '{Query}' returned {Count} results.", query, users.Count);

        return new PaginatedResult<ProfileDto>(profileDtos, totalCount, pagination.PageNumber, pagination.PageSize);
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
        var reviewCount = await appDb.Reviews.CountAsync(r => r.UserId == targetUserId && !r.PingActivity!.Ping.IsDeleted);
        var eventCount = await appDb.EventAttendees.CountAsync(ea => ea.UserId == targetUserId);
        // "Pings visited" -> distinct pings from reviews + Created Pings (filtering deleted)
        var pingVisitCount = await appDb.Reviews
            .Where(r => r.UserId == targetUserId && !r.PingActivity!.Ping.IsDeleted)
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
        var reviewCount = await appDb.Reviews.CountAsync(r => r.UserId == targetUserId && !r.PingActivity!.Ping.IsDeleted);
        var eventCount = await appDb.EventAttendees.CountAsync(ea => ea.UserId == targetUserId);
        // "Pings visited" -> distinct pings from reviews + Created Pings (filtering deleted)
        var pingVisitCount = await appDb.Reviews
            .Where(r => r.UserId == targetUserId && !r.PingActivity!.Ping.IsDeleted)
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
        // Logic: Return {Ping, Date} to allow sorting by recency
        // Created Pings
        // Created Pings (Active only)
        var createdQuery = appDb.Pings.AsNoTracking()
            .Where(p => p.OwnerUserId == targetUserId && !p.IsDeleted)
            .Select(p => new { PingId = p.Id, Date = p.CreatedUtc, p.Visibility, p.OwnerUserId, p.IsDeleted });

        // Visited Pings (from Reviews, include deleted for history)
        var visitedQuery = appDb.Reviews.AsNoTracking()
            .Where(r => r.UserId == targetUserId)
            .Select(r => new { PingId = r.PingActivity!.PingId, Date = r.CreatedAt, r.PingActivity.Ping.Visibility, r.PingActivity.Ping.OwnerUserId, r.PingActivity.Ping.IsDeleted });

        // Combined List of ID+Date+Visibility
        // EF Core 9 might still struggle with full UNION of anonymous types followed by complex conditional filtering in one go.
        // Let's materialize the lightweight list first (fetching ID, Date, Visibility) - typically small enough (<10k rows usually).
        // If huge, we need to optimize differently, but for a user profile, it's manageable.
        
        var createdList = await createdQuery.ToListAsync();
        var visitedList = await visitedQuery.ToListAsync();
        
        var combinedList = createdList.Concat(visitedList); // In-Memory concat

        // Apply Visibility Filters In-Memory
        // Public: Everyone sees
        // Owner is Me (Viewer): I see
        // Owner is Target (Profile Owner) AND IsFriend: I see
        
        var visibleItems = combinedList
            .Where(x => !x.IsDeleted || isSelf) // Show deleted pings only to self
            .Where(x => 
                x.Visibility == Models.Pings.PingVisibility.Public ||
                x.OwnerUserId == currentUserId ||
                (x.Visibility == Models.Pings.PingVisibility.Friends && x.OwnerUserId == targetUserId && isFriend)
        );

        // Group By PingId to get latest date
        var groupedItems = visibleItems
            .GroupBy(x => x.PingId)
            .Select(g => new { PingId = g.Key, Date = g.Max(x => x.Date) })
            .OrderByDescending(x => x.Date)
            .ToList();

        var totalCount = groupedItems.Count;
        
        // Paginate IDs
        var pagedIds = groupedItems
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(x => x.PingId)
            .ToList();

        if (pagedIds.Count == 0)
        {
             return new PaginatedResult<PingDetailsDto>(new List<PingDetailsDto>(), totalCount, pagination.PageNumber, pagination.PageSize);
        }

        // Fetch Full Ping Details for the page
        var pings = await appDb.Pings.AsNoTracking()
            .Where(p => pagedIds.Contains(p.Id))
            .Include(p => p.PingGenre)
            .ToListAsync();
            
        // Re-order to match date sort
        var pingsOrdered = pagedIds
            .Select(id => pings.FirstOrDefault(p => p.Id == id))
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        var pingDtos = new List<PingDetailsDto>();
        foreach (var p in pingsOrdered)
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
                p.GooglePlaceId,
                p.IsDeleted // Matches IsPingDeleted in constructor
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
            .Where(e => e.CreatedById == targetUserId)
            .Select(e => new { EventId = e.Id, e.StartTime, e.IsPublic, e.CreatedById, IsAttendee = false }); // IsAttendee not used for filter yet but needed for union shape? Actually we can fetch Id, Time etc.

        // Attending Events
        // We need to know if current user is attending for visibility check: e.Attendees.Any(a => a.UserId == currentUserId)
        // This is hard to project in a simple union.
        // Strategy: Fetch IDs and metadata, filter in memory if necessary or refine query.
        
        // Let's materialize lightweight lists first.
        var createdList = await createdQuery.Select(x => new { x.EventId, x.StartTime, x.IsPublic, x.CreatedById, IsMyAttendee = false }).ToListAsync(); 
        
        // For attending list, we just need events target user is attending.
        var attendingList = await appDb.EventAttendees.AsNoTracking()
            .Where(ea => ea.UserId == targetUserId)
            .Select(ea => new { 
                EventId = ea.EventId, 
                ea.Event.StartTime, 
                ea.Event.IsPublic, 
                ea.Event.CreatedById,
                IsMyAttendee = ea.Event.Attendees.Any(a => a.UserId == currentUserId) // Check if viewer is also attending
            })
            .ToListAsync();

        // Check if viewer is attending "Created" events (for visibility)
        // This requires an extra check or fetch. 
        // Simplification: We already fetched 'attendingList' which contains events TargetUser attends.
        // If TargetUser created an event, they might not be in Attendees list explicitly? (Depends on business logic. Usually creator assumes attendance or adds self).
        // Let's assume visibility check needs: IsPublic OR CreatedByViewer OR ViewerIsAttendee.
        
        // We need to know if Viewer Is Attendee for the 'createdList' too.
        // Use a separate query to get all EventIds viewer is attending, then check against that set.
        var viewerAttendingIds = await appDb.EventAttendees.AsNoTracking()
            .Where(ea => ea.UserId == currentUserId)
            .Select(ea => ea.EventId)
            .ToListAsync();
        var viewerAttendingSet = new HashSet<int>(viewerAttendingIds);

        var combinedList = createdList.Select(x => new { x.EventId, x.StartTime, x.IsPublic, x.CreatedById })
            .Concat(attendingList.Select(x => new { x.EventId, x.StartTime, x.IsPublic, x.CreatedById }))
            .DistinctBy(x => x.EventId); // Remove duplicates if creator is also attendee

        // Visibility Filter
        var visibleItems = combinedList.Where(e => 
            e.IsPublic || 
            e.CreatedById == currentUserId || 
            viewerAttendingSet.Contains(e.EventId)
        );

        var totalCount = visibleItems.Count();

        // Sort
        bool isAscending = sortOrder?.Equals("Asc", StringComparison.OrdinalIgnoreCase) ?? false;
        var pagedIds = (isAscending 
            ? visibleItems.OrderBy(e => e.StartTime) 
            : visibleItems.OrderByDescending(e => e.StartTime))
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(e => e.EventId)
            .ToList();

        if (pagedIds.Count == 0)
        {
             return new PaginatedResult<EventDto>(new List<EventDto>(), totalCount, pagination.PageNumber, pagination.PageSize);
        }

        var pagedEvents = await appDb.Events.AsNoTracking()
            .Where(e => pagedIds.Contains(e.Id))
            .Include(e => e.Attendees) 
            .Include(e => e.Ping)
            .ToListAsync();

        // Re-order
        var eventsOrdered = pagedIds
             .Select(id => pagedEvents.FirstOrDefault(e => e.Id == id))
             .Where(e => e != null)
             .Select(e => e!)
             .ToList();

        var eventDtos = new List<EventDto>();
        if (eventsOrdered.Any())
        {
            // Batch fetch creators
            var creatorIds = eventsOrdered.Select(e => e.CreatedById).Distinct().ToList();
            var creators = await userManager.Users
                .AsNoTracking()
                .Where(u => creatorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

            // Batch fetch attendees for mapping
            var allAttendeeIds = eventsOrdered.SelectMany(e => e.Attendees.Select(a => a.UserId)).Distinct().ToList();
            var attendeesMap = await userManager.Users
                .AsNoTracking()
                .Where(u => allAttendeeIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

            var friendIds = await followService.GetMutualIdsAsync(currentUserId);

            foreach (var evt in eventsOrdered)
            {
                if (creators.TryGetValue(evt.CreatedById, out var creator))
                {
                     // Creator summary
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

        // Blocking Check
        if (targetUserId != currentUserId)
        {
            var isBlocked = await blockService.IsBlockedAsync(currentUserId, targetUserId) ||
                            await blockService.IsBlockedAsync(targetUserId, currentUserId);
            if (isBlocked) throw new KeyNotFoundException("User not found.");
        }

        bool isSelf = targetUserId == currentUserId;
        bool isFriend = false;

        if (!isSelf)
        {
            // Calculate friendship for privacy checks
            var isFollowing = await followService.IsFollowingAsync(currentUserId, targetUserId);
            var isFollowedBy = await followService.IsFollowingAsync(targetUserId, currentUserId);
            isFriend = isFollowing && isFollowedBy;
        }

        // Determine permissions
        bool canViewReviews = isSelf || user.ReviewsPrivacy == AppUserPrivacy.Public || 
                              (user.ReviewsPrivacy == AppUserPrivacy.FriendsOnly && isFriend);
        
        bool canViewPings = isSelf || user.PingsPrivacy == AppUserPrivacy.Public || 
                            (user.PingsPrivacy == AppUserPrivacy.FriendsOnly && isFriend);

        // If both are private, and not self/friend as allowed, return empty
        if (!canViewReviews && !canViewPings)
        {
             return new PaginatedResult<PlaceReviewSummaryDto>(new List<PlaceReviewSummaryDto>(), 0, pagination.PageNumber, pagination.PageSize);
        }

        // Data Collection: Created Pings (Active only)
        var createdPingIds = canViewPings 
            ? await appDb.Pings.AsNoTracking()
                .Where(p => p.OwnerUserId == targetUserId && !p.IsDeleted)
                .Select(p => p.Id)
                .ToListAsync()
            : new List<int>();

        // Data Collection: Reviewed/Visited Pings (include deleted for history)
        var reviewedPingIds = canViewReviews
            ? await appDb.Reviews.AsNoTracking()
                .Where(r => r.UserId == targetUserId)
                .Select(r => r.PingActivity!.PingId)
                .Distinct()
                .ToListAsync()
            : new List<int>();

        var allRelevantPingIds = createdPingIds.Union(reviewedPingIds).ToList();

        if (!allRelevantPingIds.Any())
        {
            return new PaginatedResult<PlaceReviewSummaryDto>(new List<PlaceReviewSummaryDto>(), 0, pagination.PageNumber, pagination.PageSize);
        }

        // Fetch the Pings and their Reviews for these IDs
        var pings = await appDb.Pings.AsNoTracking()
            .Where(p => allRelevantPingIds.Contains(p.Id))
            .Include(p => p.PingGenre)
            .ToListAsync();

        // Only fetch reviews if the user has permission to see them
        var userReviews = new List<dynamic>();
        if (canViewReviews)
        {
             var rawReviews = await appDb.Reviews.AsNoTracking()
                .Where(r => r.UserId == targetUserId && allRelevantPingIds.Contains(r.PingActivity!.PingId))
                .Select(r => new { r.PingActivity!.PingId, r.Rating, r.ImageUrl, r.ThumbnailUrl, r.CreatedAt })
                .ToListAsync();
             userReviews = rawReviews.Cast<dynamic>().ToList();
        }

        // Grouping & Summarizing
        var reviewsByPing = userReviews.GroupBy(r => (int)r.PingId).ToDictionary(g => g.Key, g => g.ToList());

        var grouped = pings.Select(ping => {
            // Get reviews for this ping
            reviewsByPing.TryGetValue(ping.Id, out var pReviews);
            var count = pReviews?.Count ?? 0;
            var avgRating = count > 0 ? pReviews!.Average(r => (int)r.Rating) : 0;
            var latestDate = count > 0 ? pReviews!.Max(r => (DateTime)r.CreatedAt) : ping.CreatedUtc;

            return new 
            {
               Ping = ping,
               Reviews = pReviews?.Select(r => (dynamic)new { r.Rating, r.ImageUrl, r.ThumbnailUrl, r.CreatedAt }).ToList() ?? new List<dynamic>(),
               Count = count,
               AverageRating = avgRating,
               LatestDate = latestDate
            };
        });

        // Sorting
        bool isAscending = sortOrder?.Equals("Asc", StringComparison.OrdinalIgnoreCase) ?? false;
        var sortedGroups = string.Equals(sortBy, "Rating", StringComparison.OrdinalIgnoreCase)
            ? (isAscending ? grouped.OrderBy(x => x.AverageRating) : grouped.OrderByDescending(x => x.AverageRating))
            : (isAscending ? grouped.OrderBy(x => x.LatestDate) : grouped.OrderByDescending(x => x.LatestDate));

        var validGroups = new List<PlaceReviewSummaryDto>();

        foreach (var group in sortedGroups) 
        {
            // Permission Check: Delete filter
            if (group.Ping.IsDeleted && !isSelf) continue;

            // Ping Visibility Check (ensure viewer can see the Ping itself)
            bool canSeePing = false;

            if (isSelf) 
            {
                canSeePing = true; 
            }
            else
            {
                if (group.Ping.Visibility == Models.Pings.PingVisibility.Public)
                {
                    canSeePing = true;
                }
                else if (group.Ping.OwnerUserId == currentUserId)
                {
                    canSeePing = true; 
                }
                else if (group.Ping.Visibility == Models.Pings.PingVisibility.Friends)
                {
                    // Visible if Viewer is friend of Ping Owner.
                    if (group.Ping.OwnerUserId == targetUserId)
                    {
                        if (isFriend) canSeePing = true;
                    }
                    else 
                    {
                        // Third-party friend check (optional but here for completeness)
                        // If we skip it, we only show public/self-owned/target-owned pings.
                    }
                }
            }

            if (canSeePing)
            {
                var thumbnails = group.Reviews
                    .Where(r => !string.IsNullOrEmpty((string?)r.ThumbnailUrl))
                    .OrderByDescending(r => (DateTime)r.CreatedAt)
                    .Take(5)
                    .Select(r => (string)r.ThumbnailUrl!)
                    .ToList();

                // Removed fallback to Ping.ThumbnailUrl as it no longer exists.
                // If ping has no review images, thumbnails will be empty.

                validGroups.Add(new PlaceReviewSummaryDto(
                    group.Ping.Id,
                    group.Ping.Name,
                    group.Ping.Address ?? "",
                    Math.Round(group.AverageRating, 1),
                    group.Count,
                    thumbnails,
                    group.Ping.IsDeleted
                ));
            }
        }

        // Pagination
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
        if (ping == null) throw new KeyNotFoundException("Place not found.");
        
        // Allow viewing deleted pings only for the owner
        if (ping.IsDeleted && !isSelf)
        {
            throw new KeyNotFoundException("Place not found.");
        }

        if (!isSelf && !ping.IsDeleted)
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
            .Include(r => r.PingActivity)
                .ThenInclude(pa => pa.Ping)
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
                r.ImageUrl ?? "",
                r.ThumbnailUrl ?? "",
                r.CreatedAt,
                r.LikesList.Count,
                r.LikesList.Any(l => l.UserId == currentUserId),
                r.UserId == currentUserId, // IsOwner
                r.ReviewTags.Select(rt => rt.Tag.Name).ToList(),
                ping.IsDeleted
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

