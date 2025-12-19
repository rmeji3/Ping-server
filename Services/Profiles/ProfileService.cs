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
    IBlockService blockService) : IProfileService
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

        // Fetch My Upcoming Events (Public + Private)
        var upcomingEvents = await appDb.Events
            .AsNoTracking()
            .Where(e => e.CreatedById == userId && e.StartTime > DateTime.UtcNow)
            .Include(e => e.Attendees) 
            .OrderBy(e => e.StartTime)
            .ToListAsync();

        var events = new List<EventDto>();
        if (upcomingEvents.Any())
        {
            var creatorSummary = new UserSummaryDto(user.Id, user.UserName!, user.FirstName, user.LastName, user.ProfileImageUrl);
            
            var distinctUserIds = upcomingEvents.SelectMany(e => e.Attendees.Select(a => a.UserId)).Distinct().ToList();
            var attendeeUsers = await userManager.Users
                .AsNoTracking()
                .Where(u => distinctUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

            var friendIds = await followService.GetMutualIdsAsync(userId);

            foreach (var evt in upcomingEvents)
            {
                events.Add(EventMapper.MapToDto(evt, creatorSummary, attendeeUsers, userId, friendIds));
            }

        }

        // Fetch My Reviews
        var reviews = await appDb.Reviews.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Include(r => r.ReviewTags).ThenInclude(rt => rt.Tag)
            .OrderByDescending(r => r.CreatedAt)
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
                r.LikesList.Any(l => l.UserId == userId),
                r.ReviewTags.Select(rt => rt.Tag.Name).ToList()
            ))
            .ToListAsync();

        // Fetch My Pings (Created + Visited)
        // Created Pings
        var createdPingsQuery = appDb.Pings.AsNoTracking()
            .Where(p => p.OwnerUserId == userId && !p.IsDeleted)
            .Select(p => new { Ping = p, Date = p.CreatedUtc });

        // Visited Pings (from Reviews)
        var visitedPingsQuery = appDb.Reviews.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => new { Ping = r.PingActivity!.Ping, Date = r.CreatedAt })
            .Where(x => !x.Ping.IsDeleted);

        var combinedPings = await createdPingsQuery
            .Union(visitedPingsQuery)
            .GroupBy(x => x.Ping.Id)
            .Select(g => g.OrderByDescending(x => x.Date).First().Ping)
            .ToListAsync();

        var pings = new List<PingDetailsDto>();
        foreach (var p in combinedPings)
        {
            pings.Add(new PingDetailsDto(
                p.Id,
                p.Name,
                p.Address ?? string.Empty,
                p.Latitude,
                p.Longitude,
                p.Visibility,
                p.Type,
                p.OwnerUserId == userId, // IsOwner
                false, // IsFavorited - simpler to skip for "My Profile" summary or fetch if needed. Keeping it false for now as per other endpoints to avoid N+1
                0, // Favorites count - skipping for summary
                Array.Empty<PingActivitySummaryDto>(),
                p.PingGenre?.Name,
                null, // ClaimStatus not loaded here
                p.IsClaimed,
                p.PingGenreId,
                p.PingGenre?.Name,
                p.GooglePlaceId
            ));
        }

            var roles = await userManager.GetRolesAsync(user);

            return new PersonalProfileDto(
                user.Id,
                user.UserName!,
                user.FirstName,
                user.LastName,
                user.ProfileImageUrl,
                user.Email!,
                events,
                pings,
                reviews,
                roles.ToArray()
            );
    }

    public async Task<List<ProfileDto>> SearchProfilesAsync(string query, string currentUsername)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Username query parameter is required.");

        var normalized = query.ToUpper(); // match Identity normalization

        var currentUser = await userManager.FindByNameAsync(currentUsername);
        var currentUserId = currentUser?.Id;

        var queryable = userManager.Users
            .AsNoTracking()
            .Where(u => u.NormalizedUserName!.StartsWith(normalized)
            && u.NormalizedUserName != currentUsername.ToUpper());

        if (currentUserId != null)
        {
            var blacklisted = await blockService.GetBlacklistedUserIdsAsync(currentUserId);
            if (blacklisted.Count > 0)
            {
                queryable = queryable.Where(u => !blacklisted.Contains(u.Id));
            }
        }

        var users = await queryable
            .OrderBy(u => u.UserName)
            .Take(15)
            .Select(u => new ProfileDto(
                u.Id,
                u.UserName!,
                u.FirstName,
                u.LastName,
                u.ProfileImageUrl,
                null, // Reviews
                null, // Places
                null, // Events
                FriendshipStatus.None, // FriendshipStatus - not calculated for search list yet
                0, // ReviewCount
                0, // PlaceVisitCount
                0, // EventCount
                false, // IsFriends
                u.ReviewsPrivacy,
                u.PingsPrivacy,
                u.LikesPrivacy
            ))
            .ToListAsync();

        logger.LogDebug("Profile search for '{Query}' returned {Count} results.", query, users.Count);

        return users;
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

        List<ReviewDto>? reviews = null;
        List<PingDetailsDto>? pings = null;

        // Privacy - Reviews
        bool showReviews = isSelf || 
                           user.ReviewsPrivacy == AppUserPrivacy.Public || 
                           (user.ReviewsPrivacy == AppUserPrivacy.FriendsOnly && isFriend);
        
        if (showReviews) 
        {
             // Mapping Review to Dto. Simple mapping.
             // Note: ReviewDto definition?
             // public record ReviewDto(int Id, int Rating, string? Content, string UserId, string UserName, DateTime CreatedAt, int Likes, bool IsLiked, List<string> Tags);
             // We need tags and likes count.
             // For simplicity, fetching basic info.
             
             reviews = await appDb.Reviews.AsNoTracking()
                 .Where(r => r.UserId == targetUserId)
                 .Include(r => r.ReviewTags).ThenInclude(rt => rt.Tag)
                 .OrderByDescending(r => r.CreatedAt)
                 .Take(5)
                 .Select(r => new ReviewDto(
                     r.Id,
                     r.Rating,
                     r.Content,
                     r.UserId,
                     user.UserName!, // Target user name
                     user.ProfileImageUrl,
                     r.ImageUrl,
                     r.ThumbnailUrl,
                     r.CreatedAt,
                     r.LikesList.Count,
                     r.LikesList.Any(l => l.UserId == currentUserId), // IsLiked
                     r.ReviewTags.Select(rt => rt.Tag.Name).ToList()
                 ))
                 .ToListAsync();
        }

            // Privacy - Pings
            bool showPings = isSelf || 
                             user.PingsPrivacy == AppUserPrivacy.Public || 
                             (user.PingsPrivacy == AppUserPrivacy.FriendsOnly && isFriend);
            
            if (showPings)
            {
                // Fetch Created Pings
                var createdPingsQuery = appDb.Pings.AsNoTracking()
                    .Where(p => p.OwnerUserId == targetUserId && !p.IsDeleted);

                // Fetch Visited Pings (Distinct pings from reviews)
                var visitedPingsQuery = appDb.Reviews.AsNoTracking()
                    .Where(r => r.UserId == targetUserId)
                    .Select(r => r.PingActivity!.Ping)
                    .Where(p => !p.IsDeleted);

                // Combine and Deduplicate
                // Note: Union might be heavy if fields differ, but purely on ID/Entity it works for EF.
                // However, doing Union on queries with different Selects/Sources can be tricky in EF Core versions.
                // Safest is to fetch IDs or do two list fetches if lists are small, but let's try distinct ID fetch first to be efficient?
                // Or just Concat results in memory? Profiles usually have finite places.
                // Let's list specific fields needed for DTO to make it lighter.
                
                var createdPings = await createdPingsQuery.ToListAsync();
                var visitedPings = await visitedPingsQuery.ToListAsync();
                
                var allPings = createdPings.Concat(visitedPings)
                    .GroupBy(p => p.Id)
                    .Select(g => g.First())
                    .ToList();

                var visiblePings = new List<PingDetailsDto>();

                foreach (var p in allPings)
                {
                    bool isPingOwner = p.OwnerUserId == currentUserId;
                    bool isPingRefOwner = p.OwnerUserId == targetUserId; // The profile owner owns this ping

                    bool canSee = false;

                    // 1. If I own the ping, I see it.
                    if (isPingOwner)
                    {
                        canSee = true;
                    }
                    // 2. If it is Public, I see it.
                    else if (p.Visibility == Models.Pings.PingVisibility.Public)
                    {
                        canSee = true;
                    }
                    // 3. If it is Friends only...
                    else if (p.Visibility == Models.Pings.PingVisibility.Friends)
                    {
                        // Logic: Viewer must be friend of Ping Owner.
                        if (isPingRefOwner)
                        {
                            // Ping is owned by Profile Owner.
                            // We already know friendship status with Profile Owner (isFriend).
                            if (isFriend) canSee = true;
                        }
                        else
                        {
                            // Place is owned by someone else (Third Party).
                            // Viewer must be friend of Third Party.
                            // This would require checking friendship with p.OwnerUserId.
                            // To avoid N+1 DB calls for random places, we might SKIP this check or optimize.
                            // For now, to be safe/strict on privacy:
                            // If we can't verify friendship easily, assume NO access unless it's the profile owner's place.
                            // However, arguably if it's in the list, we might want to show it? NO, privacy first.
                            // Optimization: If `isFriend` (with specific user), we access.
                            // We'll skip complex check for third party friends for now (assume hidden if not public/owned).
                        }
                    }
                    // 4. Private: Only owner sees (already handled by #1).

                    if (canSee)
                    {
                        visiblePings.Add(new PingDetailsDto(
                            p.Id,
                            p.Name,
                            p.Address ?? string.Empty,
                            p.Latitude,
                            p.Longitude,
                            p.Visibility,
                            p.Type,
                            isPingOwner,
                            false, // IsFavorited
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
                }
                
                pings = visiblePings;
            }

            // Fetch Events (Upcoming)
            // Rule: 
            // - If Owner (isSelf): Show All Upcoming (Public + Private)
            // - If Visitor: Show Only Public Upcoming
            var upcomingEvents = await appDb.Events
                .AsNoTracking()
                .Where(e => e.CreatedById == targetUserId && 
                           (e.IsPublic || isSelf) && 
                           e.StartTime > DateTime.UtcNow)
                .Include(e => e.Attendees) // Needed for status check
                .OrderBy(e => e.StartTime)
                .ToListAsync();

            var events = new List<EventDto>();
            if (upcomingEvents.Any())
            {
                // Prepare Mapper dependencies
                // 1. Creator Summary (Mock/Reconstruct since we have 'user' object)
                var creatorSummary = new UserSummaryDto(user.Id, user.UserName!, user.FirstName, user.LastName, user.ProfileImageUrl);

                // 2. Attendee Map (For showing attendees in the event card - EventMapper expects this)
                // Since fetching all attendees for all events might be heavy, and Profile usually shows a summary...
                // EventMapper uses `attendeeMap` to populate `Attendees` list in DTO.
                // Optimally: fetch all attendee user IDs from these events, then fetch AppUsers.
                var distinctUserIds = upcomingEvents.SelectMany(e => e.Attendees.Select(a => a.UserId)).Distinct().ToList();
                var attendeeUsers = await userManager.Users
                    .AsNoTracking()
                    .Where(u => distinctUserIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id);

                var friendIds = await followService.GetMutualIdsAsync(currentUserId);

                foreach (var evt in upcomingEvents)
                {
                    events.Add(EventMapper.MapToDto(evt, creatorSummary, attendeeUsers, currentUserId, friendIds));
                }
            }


        return new ProfileDto(
            user.Id,
            user.UserName!,   // DisplayName (using UserName for now)
            user.FirstName,
            user.LastName,
            user.ProfileImageUrl,
            reviews,
            pings,
            events, // New Field
            friendshipStatus,
            reviewCount,
            pingVisitCount,
            eventCount,
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

        return new QuickProfileDto(
            user.Id,
            user.UserName!,   
            user.FirstName,
            user.LastName,
            user.ProfileImageUrl,
            friendshipStatus,
            reviewCount,
            pingVisitCount,
            eventCount,
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

    public async Task<PaginatedResult<EventDto>> GetUserEventsAsync(string targetUserId, string currentUserId, PaginationParams pagination)
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
        
        // No specific "EventsPrivacy" setting on User model yet (only Reviews, Places, Likes).
        // Assuming Events are public if the Event itself is Public, or if we share commonality?
        // Usually Profiles imply "Things I'm doing". 
        // If I am not friend, should I see their events? 
        // Let's assume yes, filtered by Event Visibility (Public events are visible).

        // Created Events
        var createdQuery = appDb.Events.AsNoTracking()
            .Where(e => e.CreatedById == targetUserId);

        // Attending Events
        var attendingQuery = appDb.EventAttendees.AsNoTracking()
            .Where(ea => ea.UserId == targetUserId)
            .Select(ea => ea.Event);

        var combinedQuery = createdQuery.Union(attendingQuery);

        // Visibility Filter
        // 1. Public events -> Visible
        // 2. I created the event -> Visible
        // 3. I am attending the event -> Visible
        // 4. (Optional) Friend of creator? 
        // For now: Only Public or Involved.
        combinedQuery = combinedQuery.Where(e => 
            e.IsPublic || 
            e.CreatedById == currentUserId || 
            e.Attendees.Any(a => a.UserId == currentUserId)
        );

        // Only Upcoming? Or Past too?
        // "Events" tab usually shows upcoming first. user might want history.
        // Let's default to All, Ordered by StartTime. 
        // Or if the user explicit asked for "Profile with... load as I scroll", probably wants future then past?
        // Standard: Descending StartTime (Newest/Future first) or Ascending from Now?
        // Let's sort by StartTime Descending (Show latest/future first).
        
        var totalCount = await combinedQuery.CountAsync();

        var pagedEvents = await combinedQuery
            .OrderByDescending(e => e.StartTime)
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Include(e => e.Attendees) // Need attendees for mapping
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
                    var creatorSummary = new UserSummaryDto(creator.Id, creator.UserName!, creator.FirstName, creator.LastName, creator.ProfileImageUrl);
                    eventDtos.Add(EventMapper.MapToDto(evt, creatorSummary, attendeesMap, currentUserId, friendIds));
                }
            }
        }

        return new PaginatedResult<EventDto>(eventDtos, totalCount, pagination.PageNumber, pagination.PageSize);
    }
    public async Task<PaginatedResult<PlaceReviewSummaryDto>> GetProfilePlacesAsync(string targetUserId, string currentUserId, PaginationParams pagination)
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

        foreach (var group in grouped.OrderByDescending(x => x.LatestReviewDate)) // Default sort by recency
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

