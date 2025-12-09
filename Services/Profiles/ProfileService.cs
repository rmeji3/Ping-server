using Conquest.Dtos.Profiles;
using Conquest.Models.AppUsers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Conquest.Data.App;
using Conquest.Services.Friends;
using Conquest.Models.Friends; // For FriendshipStatus enum
using FriendshipStatus = Conquest.Dtos.Profiles.FriendshipStatus; // Alias for DTO enum
using Conquest.Services.Storage;
using Conquest.Dtos.Activities; // For ActivitySummaryDto
using Conquest.Dtos.Reviews; // Fix ReviewDto
using Conquest.Dtos.Places;  // Fix PlaceDetailsDto
using Conquest.Dtos.Events; // Logic for events
using Conquest.Services.Events; // For EventMapper
using Conquest.Dtos.Common; // For PaginationParams, PaginatedResult
using Conquest.Services.Profiles;
using DtoPrivacy = Conquest.Dtos.Profiles.PrivacyConstraint; // Alias for DTO enum
using AppUserPrivacy = Conquest.Models.AppUsers.PrivacyConstraint; // Alias for Model enum

using Conquest.Services.Blocks;

namespace Conquest.Services.Profiles;

public class ProfileService(
    UserManager<AppUser> userManager, 
    ILogger<ProfileService> logger, 
    IStorageService storageService,
    AppDbContext appDb,
    IFriendService friendService,
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

            foreach (var evt in upcomingEvents)
            {
                events.Add(EventMapper.MapToDto(evt, creatorSummary, attendeeUsers, userId));
            }
        }

        return new PersonalProfileDto(
            user.Id,
            user.UserName!,
            user.FirstName,
            user.LastName,
            user.ProfileImageUrl,
            user.Email!,
            events
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
                (DtoPrivacy)u.ReviewsPrivacy,
                (DtoPrivacy)u.PlacesPrivacy,
                (DtoPrivacy)u.LikesPrivacy
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

        // Validate file
        // 5MB limit
        if (file.Length > 5 * 1024 * 1024)
        {
            throw new ArgumentException("File size exceeds 5MB limit.");
        }

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType))
        {
            throw new ArgumentException("Invalid file type. Only JPEG, PNG, and WebP are allowed.");
        }

        // Generate key: profiles/{userId}/{timestamp}-{random}.ext
        var ext = Path.GetExtension(file.FileName);
        var key = $"profiles/{userId}/{DateTime.UtcNow.Ticks}{ext}";

        // Upload
        var url = await storageService.UploadFileAsync(file, key);

        // Update User
        user.ProfileImageUrl = url;
        await userManager.UpdateAsync(user);
        
        logger.LogInformation("Updated profile image for user {UserId} to {Url}", userId, url);

        return url;
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
        
        // Friendship
        var fsStatus = await friendService.GetFriendshipStatusAsync(currentUserId, targetUserId);
        var friendshipStatus = FriendshipStatus.None;
        
        // Map Friendship.FriendshipStatus (Model) to Dto.FriendshipStatus
        if ((int)fsStatus != 999) 
        {
             // Assuming matching names/values roughly, but let's map explicitly
             switch (fsStatus)
             {
                 case Friendship.FriendshipStatus.Accepted: friendshipStatus = FriendshipStatus.Accepted; break;
                 case Friendship.FriendshipStatus.Pending: friendshipStatus = FriendshipStatus.Pending; break;
                 case Friendship.FriendshipStatus.Blocked: friendshipStatus = FriendshipStatus.Blocked; break;
             }
        }

        var isFriend = friendshipStatus == FriendshipStatus.Accepted;

        // Stats
        var reviewCount = await appDb.Reviews.CountAsync(r => r.UserId == targetUserId);
        var eventCount = await appDb.EventAttendees.CountAsync(ea => ea.UserId == targetUserId);
        // "Places visited" -> distinct places from reviews + Created Places
        var placeVisitCount = await appDb.Reviews
            .Where(r => r.UserId == targetUserId)
            .Select(r => r.PlaceActivity.PlaceId)
            .Union(appDb.Places.Where(p => p.OwnerUserId == targetUserId && !p.IsDeleted).Select(p => p.Id))
            .CountAsync();

        List<ReviewDto>? reviews = null;
        List<PlaceDetailsDto>? places = null;

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
                     r.CreatedAt,
                     r.LikesList.Count,
                     r.LikesList.Any(l => l.UserId == currentUserId), // IsLiked
                     r.ReviewTags.Select(rt => rt.Tag.Name).ToList()
                 ))
                 .ToListAsync();
        }

            // Privacy - Places
            bool showPlaces = isSelf || 
                              user.PlacesPrivacy == AppUserPrivacy.Public || 
                              (user.PlacesPrivacy == AppUserPrivacy.FriendsOnly && isFriend);
            
            if (showPlaces)
            {
                // Fetch Created Places
                var createdPlacesQuery = appDb.Places.AsNoTracking()
                    .Where(p => p.OwnerUserId == targetUserId && !p.IsDeleted);

                // Fetch Visited Places (Distinct places from reviews)
                var visitedPlacesQuery = appDb.Reviews.AsNoTracking()
                    .Where(r => r.UserId == targetUserId)
                    .Select(r => r.PlaceActivity.Place)
                    .Where(p => !p.IsDeleted);

                // Combine and Deduplicate
                // Note: Union might be heavy if fields differ, but purely on ID/Entity it works for EF.
                // However, doing Union on queries with different Selects/Sources can be tricky in EF Core versions.
                // Safest is to fetch IDs or do two list fetches if lists are small, but let's try distinct ID fetch first to be efficient?
                // Or just Concat results in memory? Profiles usually have finite places.
                // Let's list specific fields needed for DTO to make it lighter.
                
                var createdPlaces = await createdPlacesQuery.ToListAsync();
                var visitedPlaces = await visitedPlacesQuery.ToListAsync();
                
                var allPlaces = createdPlaces.Concat(visitedPlaces)
                    .GroupBy(p => p.Id)
                    .Select(g => g.First())
                    .ToList();

                var visiblePlaces = new List<PlaceDetailsDto>();

                foreach (var p in allPlaces)
                {
                    bool isPlaceOwner = p.OwnerUserId == currentUserId;
                    bool isPlaceRefOwner = p.OwnerUserId == targetUserId; // The profile owner owns this place

                    bool canSee = false;

                    // 1. If I own the place, I see it.
                    if (isPlaceOwner)
                    {
                        canSee = true;
                    }
                    // 2. If it is Public, I see it.
                    else if (p.Visibility == Models.Places.PlaceVisibility.Public)
                    {
                        canSee = true;
                    }
                    // 3. If it is Friends only...
                    else if (p.Visibility == Models.Places.PlaceVisibility.Friends)
                    {
                        // Logic: Viewer must be friend of Place Owner.
                        if (isPlaceRefOwner)
                        {
                            // Place is owned by Profile Owner.
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
                        visiblePlaces.Add(new PlaceDetailsDto(
                            p.Id,
                            p.Name,
                            p.Address ?? string.Empty,
                            p.Latitude,
                            p.Longitude,
                            p.Visibility,
                            p.Type,
                            isPlaceOwner,
                            false, // IsFavorited
                            0, // Favorites count
                            Array.Empty<ActivitySummaryDto>(),
                            Array.Empty<string>()
                        ));
                    }
                }
                
                places = visiblePlaces;
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

                foreach (var evt in upcomingEvents)
                {
                    events.Add(EventMapper.MapToDto(evt, creatorSummary, attendeeUsers, currentUserId));
                }
            }


        return new ProfileDto(
            user.Id,
            user.UserName!,   // DisplayName (using UserName for now)
            user.FirstName,
            user.LastName,
            user.ProfileImageUrl,
            reviews,
            places,
            events, // New Field
            friendshipStatus,
            reviewCount,
            placeVisitCount,
            eventCount,
            isFriend,
            (DtoPrivacy)user.ReviewsPrivacy,
            (DtoPrivacy)user.PlacesPrivacy,
            (DtoPrivacy)user.LikesPrivacy
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
        var fsStatus = await friendService.GetFriendshipStatusAsync(currentUserId, targetUserId);
        var friendshipStatus = FriendshipStatus.None;
        
        if ((int)fsStatus != 999) 
        {
             switch (fsStatus)
             {
                 case Friendship.FriendshipStatus.Accepted: friendshipStatus = FriendshipStatus.Accepted; break;
                 case Friendship.FriendshipStatus.Pending: friendshipStatus = FriendshipStatus.Pending; break;
                 case Friendship.FriendshipStatus.Blocked: friendshipStatus = FriendshipStatus.Blocked; break;
             }
        }
        var isFriend = friendshipStatus == FriendshipStatus.Accepted;

        // Stats
        var reviewCount = await appDb.Reviews.CountAsync(r => r.UserId == targetUserId);
        var eventCount = await appDb.EventAttendees.CountAsync(ea => ea.UserId == targetUserId);
        // "Places visited" -> distinct places from reviews + Created Places
        var placeVisitCount = await appDb.Reviews
            .Where(r => r.UserId == targetUserId)
            .Select(r => r.PlaceActivity.PlaceId)
            .Union(appDb.Places.Where(p => p.OwnerUserId == targetUserId && !p.IsDeleted).Select(p => p.Id))
            .CountAsync();

        return new QuickProfileDto(
            user.Id,
            user.UserName!,   
            user.FirstName,
            user.LastName,
            user.ProfileImageUrl,
            friendshipStatus,
            reviewCount,
            placeVisitCount,
            eventCount,
            isFriend,
            (DtoPrivacy)user.ReviewsPrivacy,
            (DtoPrivacy)user.PlacesPrivacy,
            (DtoPrivacy)user.LikesPrivacy
        );
    }
    public async Task<PaginatedResult<PlaceDetailsDto>> GetUserPlacesAsync(string targetUserId, string currentUserId, PaginationParams pagination)
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
        var fsStatus = await friendService.GetFriendshipStatusAsync(currentUserId, targetUserId);
        var isFriend = fsStatus == Friendship.FriendshipStatus.Accepted;
        var isSelf = targetUserId == currentUserId;

        // If not self, check privacy settings
        if (!isSelf)
        {
            bool canViewPlaces = user.PlacesPrivacy == AppUserPrivacy.Public ||
                                 (user.PlacesPrivacy == AppUserPrivacy.FriendsOnly && isFriend);
            if (!canViewPlaces)
            {
                // Return empty if privacy restricts access
                return new PaginatedResult<PlaceDetailsDto>(new List<PlaceDetailsDto>(), 0, pagination.PageNumber, pagination.PageSize);
            }
        }

        // Logic: Return {Place, Date} to allow sorting by recency
        // Created Places
        var createdQuery = appDb.Places.AsNoTracking()
            .Where(p => p.OwnerUserId == targetUserId && !p.IsDeleted)
            .Select(p => new { Place = p, Date = p.CreatedUtc });

        // Visited Places (from Reviews)
        var visitedQuery = appDb.Reviews.AsNoTracking()
            .Where(r => r.UserId == targetUserId)
            .Select(r => new { Place = r.PlaceActivity.Place, Date = r.CreatedAt })
            .Where(x => !x.Place.IsDeleted);

        var combinedQuery = createdQuery.Union(visitedQuery);

        // Apply Visibility Filters to the combined list (to hide private/friends-only places user shouldn't see)
        // Public: Everyone sees
        // Owner is Me (Viewer): I see
        // Owner is Target (Profile Owner) AND IsFriend: I see (because of 'friends only' visibility logic on the place + we are friends)
        // Note: Logic copied from GetProfileByIdAsync but adapted for LINQ
        combinedQuery = combinedQuery.Where(x => 
            x.Place.Visibility == Models.Places.PlaceVisibility.Public ||
            x.Place.OwnerUserId == currentUserId ||
            (x.Place.Visibility == Models.Places.PlaceVisibility.Friends && x.Place.OwnerUserId == targetUserId && isFriend)
            // Note: We exclude third-party friends-only places if we aren't the owner, as consistent with GetProfile logic
        );

        var totalCount = await combinedQuery.Select(x => x.Place.Id).Distinct().CountAsync();
        
        // Paginate - distinct by Place ID to avoid duplicates if visited + created same place? 
        // Union handles distinct on the anonymous object {Place, Date}. 
        // If created and visited at different times, they appear twice? Yes.
        // We probably want unique Places.
        // GroupBy ID and take latest date.
        
        var pagedItems = await combinedQuery
            .GroupBy(x => x.Place.Id)
            .Select(g => g.OrderByDescending(x => x.Date).First()) // Take most recent interaction
            .OrderByDescending(x => x.Date)
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(x => x.Place)
            .ToListAsync();

        var placeDtos = new List<PlaceDetailsDto>();
        foreach (var p in pagedItems)
        {
            bool isPlaceOwner = p.OwnerUserId == currentUserId;
            // Map
            placeDtos.Add(new PlaceDetailsDto(
                p.Id,
                p.Name,
                p.Address ?? string.Empty,
                p.Latitude,
                p.Longitude,
                p.Visibility,
                p.Type,
                isPlaceOwner,
                false, // IsFavorited - fetching this requires extra query, skipping for list view or need batch check
                0, // Favorites count
                Array.Empty<ActivitySummaryDto>(),
                Array.Empty<string>()
            ));
        }

        return new PaginatedResult<PlaceDetailsDto>(placeDtos, totalCount, pagination.PageNumber, pagination.PageSize);
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

            foreach (var evt in pagedEvents)
            {
                if (creators.TryGetValue(evt.CreatedById, out var creator))
                {
                    var creatorSummary = new UserSummaryDto(creator.Id, creator.UserName!, creator.FirstName, creator.LastName, creator.ProfileImageUrl);
                    eventDtos.Add(EventMapper.MapToDto(evt, creatorSummary, attendeesMap, currentUserId));
                }
            }
        }

        return new PaginatedResult<EventDto>(eventDtos, totalCount, pagination.PageNumber, pagination.PageSize);
    }
}
