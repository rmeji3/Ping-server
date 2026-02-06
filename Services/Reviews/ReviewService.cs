using Ping.Data.App;
using Ping.Dtos.Common;
using Ping.Dtos.Reviews;
using Ping.Models.AppUsers;
using Ping.Models.Reviews;
using Microsoft.AspNetCore.Identity;
using Ping.Services.Follows;
using Ping.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ping.Models.Pings;

using Ping.Services.Blocks;

using Ping.Services.Notifications;
using Ping.Models;

namespace Ping.Services.Reviews;

public class ReviewService(
    AppDbContext appDb,
    IFollowService followService,
    UserManager<AppUser> userManager,
    Ping.Services.Moderation.IModerationService moderationService,
    IBlockService blockService,
    INotificationService notificationService,
    ILogger<ReviewService> logger) : IReviewService
{
    public async Task<ReviewDto> CreateReviewAsync(int pingActivityId, CreateReviewDto dto, string userId, string userName)
    {
        // Ensure activity exists
        var activityExists = await appDb.PingActivities
            .AnyAsync(pa => pa.Id == pingActivityId);

        if (!activityExists)
        {
            logger.LogWarning("CreateReview: Activity {PingActivityId} not found.", pingActivityId);
            throw new KeyNotFoundException("Activity not found.");
        }

        // Check if user already has a review for this activity
        var hasReview = await appDb.Reviews
            .AnyAsync(r => r.PingActivityId == pingActivityId && r.UserId == userId);

        if (dto.Rating < 1 || dto.Rating > 5)
        {
            logger.LogWarning("CreateReview: Invalid rating {Rating} for activity {PingActivityId} by {UserName}", dto.Rating, pingActivityId, userName);
            throw new ArgumentException("Rating must be between 1 and 5.");
        }

        if (dto.Content?.Length > 1000)
        {
            logger.LogWarning("CreateReview: Content too long for activity {PingActivityId} by {UserName}", pingActivityId, userName);
            throw new ArgumentException("Content must be at most 1000 characters.");
        }

        // Moderation Check for Content
        if (!string.IsNullOrWhiteSpace(dto.Content))
        {
            var modResult = await moderationService.CheckContentAsync(dto.Content);
            if (modResult.IsFlagged)
            {
                logger.LogWarning("Review content flagged: {Reason}", modResult.Reason);
                throw new ArgumentException($"Content rejected: {modResult.Reason}");
            }
        }

        var review = new Review
        {
            PingActivityId = pingActivityId,
            UserId = userId,
            UserName = userName,
            Rating = dto.Rating,
            Type = hasReview ? ReviewType.CheckIn : ReviewType.Review,
            Content = dto.Content,
            ImageUrl = dto.ImageUrl!,
            ThumbnailUrl = !string.IsNullOrWhiteSpace(dto.ThumbnailUrl) ? dto.ThumbnailUrl : dto.ImageUrl!,
            CreatedAt = DateTime.UtcNow,
            Likes = 0,
        };

        // Handle Tags
        if (dto.Tags != null && dto.Tags.Count > 0)
        {
            var distinctTags = dto.Tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            foreach (var tagName in distinctTags)
            {
                // Find or create tag
                var tag = await appDb.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
                if (tag == null)
                {
                    // Moderate new tag name
                    var tagMod = await moderationService.CheckContentAsync(tagName);
                    if (tagMod.IsFlagged)
                    {
                        logger.LogWarning("Tag creation flagged: {TagName} - {Reason}", tagName, tagMod.Reason);
                        continue; // Skip this bad tag, don't block entire review
                    }

                    tag = new Tag { Name = tagName };
                    appDb.Tags.Add(tag);
                }

                review.ReviewTags.Add(new ReviewTag { Tag = tag });
            }
        }

        appDb.Reviews.Add(review);
        await appDb.SaveChangesAsync();

        logger.LogInformation("Review created for Activity {PingActivityId} by {UserName}. Rating: {Rating}", pingActivityId, userName, dto.Rating);

        var user = await userManager.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        string? profileUrl = user?.ProfileImageUrl;

        // Fetch the ping's deleted status (PingActivity is not loaded on the review entity)
        var pingIsDeleted = await appDb.PingActivities.AsNoTracking()
            .Where(pa => pa.Id == pingActivityId)
            .Select(pa => pa.Ping.IsDeleted)
            .FirstOrDefaultAsync();

        return new ReviewDto(
            review.Id,
            review.Rating,
            review.Content,
            review.UserId,
            review.UserName,
            profileUrl,
            review.ImageUrl ?? "",
            review.ThumbnailUrl ?? "",
            review.CreatedAt,
            review.Likes,
            false, // IsLiked
            true, // IsOwner
            dto.Tags ?? new List<string>(), // Return tags
            pingIsDeleted
        );
    }

    public async Task<PaginatedResult<ReviewDto>> GetReviewsAsync(int pingActivityId, string scope, string userId, PaginationParams pagination)
    {
        var activityExists = await appDb.PingActivities
            .AnyAsync(pa => pa.Id == pingActivityId);
        if (!activityExists)
            throw new KeyNotFoundException("Activity not found.");

        var query = appDb.Reviews
            .AsNoTracking()
            .Include(r => r.PingActivity)
                .ThenInclude(pa => pa.Ping)
            .Include(r => r.ReviewTags)
                .ThenInclude(rt => rt.Tag)
            .Where(r => r.PingActivityId == pingActivityId)
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();

        // Filter Blacklisted Users
        var blacklistedIds = await blockService.GetBlacklistedUserIdsAsync(userId);
        if (blacklistedIds.Count > 0)
        {
            query = query.Where(r => !blacklistedIds.Contains(r.UserId));
        }

        switch (scope.ToLowerInvariant())
        {
            case "mine":
                {
                    query = query.Where(r => r.UserId == userId);
                    break;
                }
            case "friends":
                {
                    var following = await followService.GetFollowingAsync(userId, new PaginationParams { PageNumber = 1, PageSize = 1000 });
                    var friendIds = following.Items.Select(f => f.Id).ToHashSet();
                    if (friendIds.Count == 0)
                    {
                        return new PaginatedResult<ReviewDto>(new List<ReviewDto>(), 0, pagination.PageNumber, pagination.PageSize);
                    }
                    query = query.Where(r => friendIds.Contains(r.UserId));
                    break;
                }
            case "global":
            default:
                break;
        }

        var count = await query.CountAsync();

        var reviews = await query
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var reviewIds = reviews.Select(r => r.Id).ToList();
        var likedReviewIds = new HashSet<int>();
        
        if (!string.IsNullOrEmpty(userId))
        {
            likedReviewIds = await appDb.ReviewLikes
                .Where(rl => rl.UserId == userId && reviewIds.Contains(rl.ReviewId))
                .Select(rl => rl.ReviewId)
                .ToHashSetAsync();
        }

        var userIds = reviews.Select(r => r.UserId).Distinct().ToList();
        var userMap = new Dictionary<string, string?>();
        if (userIds.Count > 0)
        {
             var users = await userManager.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.ProfileImageUrl })
                .ToListAsync();
             
             foreach(var u in users) userMap[u.Id] = u.ProfileImageUrl;
        }

        var reviewDtos = reviews.Select(r => new ReviewDto(
            r.Id,
            r.Rating,
            r.Content,
            r.UserId,
            r.UserName,
            userMap.GetValueOrDefault(r.UserId),
            r.ImageUrl ?? "",
            r.ThumbnailUrl ?? "",
            r.CreatedAt,
            r.Likes,
            likedReviewIds.Contains(r.Id), 
            r.UserId == userId, // IsOwner
            r.ReviewTags.Select(rt => rt.Tag.Name).ToList(),
            r.PingActivity!.Ping.IsDeleted
        )).ToList();

        return new PaginatedResult<ReviewDto>(reviewDtos, count, pagination.PageNumber, pagination.PageSize);
    }

    public async Task<PaginatedResult<ExploreReviewDto>> GetExploreReviewsAsync(ExploreReviewsFilterDto filter, string? userId, PaginationParams pagination)
    {
        var scope = filter.Scope?.ToLowerInvariant() ?? "global";

        var query = appDb.Reviews
            .AsNoTracking()
            .Include(r => r.PingActivity)
                .ThenInclude(pa => pa.Ping)
                    .ThenInclude(p => p.PingGenre)
            .Include(r => r.ReviewTags)
                .ThenInclude(rt => rt.Tag)
            .Where(r => !r.PingActivity.Ping.IsDeleted)
            .AsQueryable();

        if (scope == "friends" && userId != null)
        {
            var following = await followService.GetFollowingAsync(userId, new PaginationParams { PageNumber = 1, PageSize = 1000 });
            var friendIds = following.Items.Select(f => f.Id).ToHashSet();
            
            query = query.Where(r => friendIds.Contains(r.UserId));
            // For friends feed, we show Public pings OR Friends-only pings 
            // (Note: This is a simplified check; strict friendship with Ping Owner is usually checked at specific access, 
            // but for feed consistency with GetFriendsFeedAsync, we allow Public/Friends visibility here)
            query = query.Where(r => r.PingActivity.Ping.Visibility == PingVisibility.Public || r.PingActivity.Ping.Visibility == PingVisibility.Friends);
        }
        else
        {
            // Global scope: Only Public pings
            query = query.Where(r => r.PingActivity.Ping.Visibility == PingVisibility.Public);
        }

        if (userId != null)
        {
            var blacklistedIds = await blockService.GetBlacklistedUserIdsAsync(userId);
            if (blacklistedIds.Count > 0)
            {
                query = query.Where(r => !blacklistedIds.Contains(r.UserId));
            }
        }

        // Filter by Search Query
        if (!string.IsNullOrWhiteSpace(filter.SearchQuery))
        {
            var search = filter.SearchQuery.Trim().ToLower();
            query = query.Where(r => 
                (r.Content != null && r.Content.ToLower().Contains(search)) ||
                r.PingActivity.Name.ToLower().Contains(search) ||
                r.PingActivity.Ping.Name.ToLower().Contains(search));
        }

        // Filter by PingGenre
        if (filter.PingGenreIds != null && filter.PingGenreIds.Any())
        {
            query = query.Where(r => r.PingActivity.Ping.PingGenreId.HasValue &&
                                     filter.PingGenreIds.Contains(r.PingActivity.Ping.PingGenreId.Value));
        }




        // Filter by Tags
        if (filter.Tags != null && filter.Tags.Any())
        {
             var tags = filter.Tags.Where(t => !string.IsNullOrWhiteSpace(t))
                                   .Select(t => t.Trim().ToLower())
                                   .ToHashSet();

             if (tags.Count > 0)
             {
                 query = query.Where(r => r.ReviewTags.Any(rt => tags.Contains(rt.Tag.Name.ToLower())));
             }
        }

        // Filter by Location (Bounding Box)
        if (filter.Latitude.HasValue && filter.Longitude.HasValue && filter.RadiusKm.HasValue)
        {
            var lat = filter.Latitude.Value;
            var lon = filter.Longitude.Value;
            var radius = filter.RadiusKm.Value;
            
            var latDelta = radius / 111.0;
            var cosLat = Math.Cos(lat * (Math.PI / 180.0));
            var lonDelta = radius / (111.0 * (Math.Abs(cosLat) < 0.0001 ? 1 : cosLat));

            var minLat = lat - latDelta;
            var maxLat = lat + latDelta;
            var minLon = lon - lonDelta;
            var maxLon = lon + lonDelta;

            query = query.Where(r => r.PingActivity.Ping.Latitude >= minLat && r.PingActivity.Ping.Latitude <= maxLat &&
                                     r.PingActivity.Ping.Longitude >= minLon && r.PingActivity.Ping.Longitude <= maxLon);
        }

        if (scope == "friends")
        {
            query = query.OrderByDescending(r => r.CreatedAt);
        }
        else
        {
            query = query.OrderByDescending(r => r.Likes);
        }

        var count = await query.CountAsync();
        var reviews = await query
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var reviewIds = reviews.Select(r => r.Id).ToList();
        var likedReviewIds = new HashSet<int>();
        
        if (userId != null)
        {
            likedReviewIds = await appDb.ReviewLikes
                .Where(rl => rl.UserId == userId && reviewIds.Contains(rl.ReviewId))
                .Select(rl => rl.ReviewId)
                .ToHashSetAsync();
        }

        var userIds = reviews.Select(r => r.UserId).Distinct().ToList();
        var userMap = new Dictionary<string, string?>();
        if (userIds.Count > 0)
        {
             var users = await userManager.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.ProfileImageUrl })
                .ToListAsync();
             foreach(var u in users) userMap[u.Id] = u.ProfileImageUrl;
        }

        var result = reviews.Select(r => new ExploreReviewDto(
            r.Id,
            r.PingActivityId,
            r.PingActivity.PingId,
            r.PingActivity.Ping.Name,
            r.PingActivity.Ping.Address ?? string.Empty,
            r.PingActivity.Name,
            r.PingActivity.Ping.PingGenre?.Name,
            r.PingActivity.Ping.Latitude,
            r.PingActivity.Ping.Longitude,
            r.Rating,
            r.Content,
            r.UserId,
            r.UserName,
            userMap.GetValueOrDefault(r.UserId),
            r.ImageUrl ?? "",
            r.ThumbnailUrl ?? "",
            r.CreatedAt,
            r.Likes,
            likedReviewIds.Contains(r.Id), 
            r.UserId == userId, // IsOwner
            r.ReviewTags.Select(rt => rt.Tag.Name).ToList(), 
            r.PingActivity.Ping.IsDeleted
        )).ToList();

        logger.LogInformation("Explore reviews fetched: Page {PageNumber}, Size {PageSize}, Count {Count}", 
            pagination.PageNumber, pagination.PageSize, count);

        return new PaginatedResult<ExploreReviewDto>(result, count, pagination.PageNumber, pagination.PageSize);
    }

    public async Task LikeReviewAsync(int reviewId, string userId)
    {
        var reviewExists = await appDb.Reviews.AnyAsync(r => r.Id == reviewId);
        if (!reviewExists)
        {
            throw new KeyNotFoundException("Review not found.");
        }

        var existingLike = await appDb.ReviewLikes
            .FirstOrDefaultAsync(rl => rl.ReviewId == reviewId && rl.UserId == userId);

        if (existingLike != null)
        {
            logger.LogInformation("Review {ReviewId} already liked by {UserId}", reviewId, userId);
            return;
        }

        var like = new ReviewLike
        {
            ReviewId = reviewId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        appDb.ReviewLikes.Add(like);

        var review = await appDb.Reviews.FindAsync(reviewId);
        if (review != null)
        {
            review.Likes++;
        }

        await appDb.SaveChangesAsync();
        logger.LogInformation("Review {ReviewId} liked by {UserId}", reviewId, userId);

        if (review != null && review.UserId != userId)
        {
            var sender = await userManager.FindByIdAsync(userId);
            await notificationService.SendNotificationAsync(new Notification
            {
                UserId = review.UserId,
                SenderId = userId,
                SenderName = sender?.UserName ?? "Someone",
                SenderProfileImageUrl = sender?.ProfileImageUrl,
                Type = NotificationType.ReviewLike,
                Title = "New Like",
                Message = $"{sender?.UserName ?? "Someone"} liked your review.",
                ReferenceId = reviewId.ToString()
            });
        }
    }

    public async Task UnlikeReviewAsync(int reviewId, string userId)
    {
        var like = await appDb.ReviewLikes
            .FirstOrDefaultAsync(rl => rl.ReviewId == reviewId && rl.UserId == userId);

        if (like == null)
        {
            logger.LogInformation("Review {ReviewId} not liked by {UserId}, nothing to unlike", reviewId, userId);
            return;
        }

        appDb.ReviewLikes.Remove(like);

        var review = await appDb.Reviews.FindAsync(reviewId);
        if (review != null && review.Likes > 0)
        {
            review.Likes--;
        }

        await appDb.SaveChangesAsync();
        logger.LogInformation("Review {ReviewId} unliked by {UserId}", reviewId, userId);
    }

    public async Task<PaginatedResult<ExploreReviewDto>> GetUserLikesAsync(string targetUserId, string viewerUserId, PaginationParams pagination, string? sortBy = null, string? sortOrder = null)
    {
        var targetUser = await userManager.FindByIdAsync(targetUserId);
        if (targetUser == null) throw new KeyNotFoundException("User not found.");

        // 1. Block Check
        if (targetUserId != viewerUserId)
        {
             var isBlocked = await blockService.IsBlockedAsync(viewerUserId, targetUserId) ||
                            await blockService.IsBlockedAsync(targetUserId, viewerUserId);
             if (isBlocked) throw new KeyNotFoundException("User not found.");
        }

        // 2. Privacy Check (LikesPrivacy)
        bool isSelf = targetUserId == viewerUserId;
        if (!isSelf)
        {
            bool isFriend = false;
            if (targetUser.LikesPrivacy == PrivacyConstraint.FriendsOnly)
            {
                var isFollowing = await followService.IsFollowingAsync(viewerUserId, targetUserId);
                var isFollowedBy = await followService.IsFollowingAsync(targetUserId, viewerUserId);
                isFriend = isFollowing && isFollowedBy;
            }

            bool canSeeLikes = targetUser.LikesPrivacy == PrivacyConstraint.Public ||
                               (targetUser.LikesPrivacy == PrivacyConstraint.FriendsOnly && isFriend);
            
            if (!canSeeLikes) return new PaginatedResult<ExploreReviewDto>(new List<ExploreReviewDto>(), 0, pagination.PageNumber, pagination.PageSize);
        }

        // 3. Fetch Likes
        var query = appDb.ReviewLikes
            .Where(rl => rl.UserId == targetUserId)
            .Include(rl => rl.Review)
                .ThenInclude(r => r.PingActivity)
                    .ThenInclude(pa => pa.Ping)
                        .ThenInclude(p => p.PingGenre)
            .Include(rl => rl.Review)
                .ThenInclude(r => r.ReviewTags)
                    .ThenInclude(rt => rt.Tag)
            .AsNoTracking()
            .Where(rl => !rl.Review.PingActivity.Ping.IsDeleted);

        // 4. Filter by Content Visibility (Ping Visibility)
        // If viewing someone else's likes, you should ONLY see likes on Public/Visible pings.
        // You generally shouldn't see that they liked a private ping unless you also have access, 
        // but typically "Likes" tab is cleaner if restricted to Public pings or heavily filtered.
        // Let's implement standard visibility check:
        // Visible if: Ping Public OR Owner is Viewer OR (Ping Friends & Owner Friend of Viewer)
        // For efficiency, let's filter to Public-Only for now if not self? 
        // Or do in-memory filter if complex.
        // Let's do standard filter query.

        if (!isSelf)
        {
            // Simplified Visibility for aggregation lists: Public Only usually?
            // "The 'Likes' list will only show reviews on **Public Pings** or Pings visible to the viewer."
            
            // To do this strictly in SQL is hard with friendship logic for every ping owner.
            // Let's fetch and filter in memory or restrict to Public + OwnedByViewer.
            // Assuming "Public" covers 90% of cases.
            
            // NOTE: We cannot easily check "Is Viewer Friend of Ping Owner" in LINQ efficiently for diverse owners.
            // So we will restrict to: Public Pings OR Pings Owned by Viewer OR Pings Owned by Target (if Target is Friend).
            
            // Let's fetch a slightly larger batch and filter? Or just Public for safety/speed.
            // Let's go with Public + OwnedByViewer.
            
            query = query.Where(rl => 
                rl.Review.PingActivity.Ping.Visibility == PingVisibility.Public ||
                rl.Review.PingActivity.Ping.OwnerUserId == viewerUserId
            );
        }

        // Default Sort: CreatedAt Descending (Recency)
        bool isAscending = sortOrder?.Equals("Asc", StringComparison.OrdinalIgnoreCase) ?? false;
        
        query = isAscending 
            ? query.OrderBy(rl => rl.CreatedAt) 
            : query.OrderByDescending(rl => rl.CreatedAt);

        var count = await query.CountAsync();
        var likedReviews = await query
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        // 5. Build DTOs
        var reviewIds = likedReviews.Select(r => r.ReviewId).ToList();
        var viewerLikedIds = new HashSet<int>();
        if (reviewIds.Count > 0)
        {
             viewerLikedIds = await appDb.ReviewLikes
                .Where(rl => rl.UserId == viewerUserId && reviewIds.Contains(rl.ReviewId))
                .Select(rl => rl.ReviewId)
                .ToHashSetAsync();
        }

        var userIds = likedReviews.Select(r => r.Review.UserId).Distinct().ToList();
        var userMap = new Dictionary<string, string?>();
        if (userIds.Count > 0)
        {
             var users = await userManager.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.ProfileImageUrl })
                .ToListAsync();
             foreach(var u in users) userMap[u.Id] = u.ProfileImageUrl;
        }
        
        var result = likedReviews.Select(rl => new ExploreReviewDto(
                rl.Review.Id,
                rl.Review.PingActivityId,
                rl.Review.PingActivity.PingId,
                rl.Review.PingActivity.Ping.Name,
                rl.Review.PingActivity.Ping.Address ?? string.Empty,
                rl.Review.PingActivity.Name,
                rl.Review.PingActivity.Ping.PingGenre?.Name,
                rl.Review.PingActivity.Ping.Latitude,
                rl.Review.PingActivity.Ping.Longitude,
                rl.Review.Rating,
                rl.Review.Content,
                rl.Review.UserId,
                rl.Review.UserName,
                userMap.GetValueOrDefault(rl.Review.UserId),
                rl.Review.ImageUrl ?? "",
                rl.Review.ThumbnailUrl ?? "",
                rl.Review.CreatedAt,
                rl.Review.Likes,
                viewerLikedIds.Contains(rl.Review.Id), // IsLiked by Viewer
                rl.Review.UserId == viewerUserId, // IsOwner
                rl.Review.ReviewTags.Select(rt => rt.Tag.Name).ToList(),
                rl.Review.PingActivity.Ping.IsDeleted
            )).ToList();

        return result.ToPaginatedResult(pagination);
    }

    public async Task<PaginatedResult<ExploreReviewDto>> GetLikedReviewsAsync(string userId, PaginationParams pagination, string? sortBy = null, string? sortOrder = null)
    {
        var query = appDb.ReviewLikes
            .Where(rl => rl.UserId == userId)
            .Include(rl => rl.Review)
                .ThenInclude(r => r.PingActivity)
                    .ThenInclude(pa => pa.Ping)
                        .ThenInclude(p => p.PingGenre)
            .Include(rl => rl.Review)
                .ThenInclude(r => r.ReviewTags)
                    .ThenInclude(rt => rt.Tag)
            .AsNoTracking();

        // Default Sort: CreatedAt Descending
        bool isAscending = sortOrder?.Equals("Asc", StringComparison.OrdinalIgnoreCase) ?? false;

        query = isAscending
            ? query.OrderBy(rl => rl.CreatedAt)
            : query.OrderByDescending(rl => rl.CreatedAt);

        var count = await query.CountAsync();

        var likedReviews = await query
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var userIds = likedReviews.Select(r => r.Review.UserId).Distinct().ToList();
        var userMap = new Dictionary<string, string?>();
        if (userIds.Count > 0)
        {
             var users = await userManager.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.ProfileImageUrl })
                .ToListAsync();
             foreach(var u in users) userMap[u.Id] = u.ProfileImageUrl;
        }

        var result = likedReviews.Select(rl => new ExploreReviewDto(
                rl.Review.Id,
                rl.Review.PingActivityId,
                rl.Review.PingActivity.PingId,
                rl.Review.PingActivity.Ping.Name,
                rl.Review.PingActivity.Ping.Address ?? string.Empty,
                rl.Review.PingActivity.Name,
                rl.Review.PingActivity.Ping.PingGenre?.Name,
                rl.Review.PingActivity.Ping.Latitude,
                rl.Review.PingActivity.Ping.Longitude,
                rl.Review.Rating,
                rl.Review.Content,
                rl.Review.UserId,
                rl.Review.UserName,
                userMap.GetValueOrDefault(rl.Review.UserId),
                rl.Review.ImageUrl ?? "",
                rl.Review.ThumbnailUrl ?? "",
                rl.Review.CreatedAt,
                rl.Review.Likes,
                true, // IsLiked
                rl.Review.UserId == userId, // IsOwner
                rl.Review.ReviewTags.Select(rt => rt.Tag.Name).ToList(),
                rl.Review.PingActivity.Ping.IsDeleted
            )).ToList();

        logger.LogInformation("Liked reviews for {UserId} retrieved: {Count} reviews", userId, result.Count);

        return result.ToPaginatedResult(pagination);
    }
    public async Task<PaginatedResult<ExploreReviewDto>> GetMyReviewsAsync(string userId, PaginationParams pagination)
    {
        var myReviews = await appDb.Reviews
            .Where(r => r.UserId == userId)
            .Include(r => r.PingActivity)
                .ThenInclude(pa => pa.Ping)
                    .ThenInclude(p => p.PingGenre)
            .Include(r => r.ReviewTags)
                .ThenInclude(rt => rt.Tag)
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var reviewIds = myReviews.Select(r => r.Id).ToList();
        var likedReviewIds = new HashSet<int>();
        
        if (reviewIds.Count > 0)
        {
            likedReviewIds = await appDb.ReviewLikes
                .Where(rl => rl.UserId == userId && reviewIds.Contains(rl.ReviewId))
                .Select(rl => rl.ReviewId)
                .ToHashSetAsync();
        }

        var userIds = myReviews.Select(r => r.UserId).Distinct().ToList();
        var userMap = new Dictionary<string, string?>();
        if (userIds.Count > 0)
        {
             var users = await userManager.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.ProfileImageUrl })
                .ToListAsync();
             foreach(var u in users) userMap[u.Id] = u.ProfileImageUrl;
        }

        var result = myReviews.Select(r => new ExploreReviewDto(
            r.Id,
            r.PingActivityId,
            r.PingActivity.PingId,
            r.PingActivity.Ping.Name,
            r.PingActivity.Ping.Address ?? string.Empty,
            r.PingActivity.Name,
            r.PingActivity.Ping.PingGenre?.Name,
            r.PingActivity.Ping.Latitude,
            r.PingActivity.Ping.Longitude,
            r.Rating,
            r.Content,
            r.UserId,
            r.UserName,
            userMap.GetValueOrDefault(r.UserId),
            r.ImageUrl ?? "",
            r.ThumbnailUrl ?? "",
            r.CreatedAt,
            r.Likes,
            likedReviewIds.Contains(r.Id),
            true, // IsOwner (My Review)
            r.ReviewTags.Select(rt => rt.Tag.Name).ToList(),
            r.PingActivity.Ping.IsDeleted
        )).ToList();

        logger.LogInformation("My reviews fetched for {UserId}: {Count} reviews", userId, result.Count);

        return result.ToPaginatedResult(pagination);
    }

    public async Task<PaginatedResult<ExploreReviewDto>> GetUserReviewsAsync(string targetUserId, string currentUserId, PaginationParams pagination)
    {
        var userReviews = await appDb.Reviews
            .Where(r => r.UserId == targetUserId)
            .Include(r => r.PingActivity)
                .ThenInclude(pa => pa.Ping)
                    .ThenInclude(p => p.PingGenre)
            .Include(r => r.ReviewTags)
                .ThenInclude(rt => rt.Tag)
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var count = await appDb.Reviews.CountAsync(r => r.UserId == targetUserId);

        var reviewIds = userReviews.Select(r => r.Id).ToList();
        var likedReviewIds = new HashSet<int>();
        
        if (reviewIds.Count > 0 && !string.IsNullOrEmpty(currentUserId))
        {
            likedReviewIds = await appDb.ReviewLikes
                .Where(rl => rl.UserId == currentUserId && reviewIds.Contains(rl.ReviewId))
                .Select(rl => rl.ReviewId)
                .ToHashSetAsync();
        }

        var userIds = userReviews.Select(r => r.UserId).Distinct().ToList();
        var userMap = new Dictionary<string, string?>();
        if (userIds.Count > 0)
        {
             var users = await userManager.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.ProfileImageUrl })
                .ToListAsync();
             foreach(var u in users) userMap[u.Id] = u.ProfileImageUrl;
        }

        var result = userReviews.Select(r => new ExploreReviewDto(
            r.Id,
            r.PingActivityId,
            r.PingActivity.PingId,
            r.PingActivity.Ping.Name,
            r.PingActivity.Ping.Address ?? string.Empty,
            r.PingActivity.Name,
            r.PingActivity.Ping.PingGenre?.Name,
            r.PingActivity.Ping.Latitude,
            r.PingActivity.Ping.Longitude,
            r.Rating,
            r.Content,
            r.UserId,
            r.UserName,
            userMap.GetValueOrDefault(r.UserId),
            r.ImageUrl ?? "",
            r.ThumbnailUrl ?? "",
            r.CreatedAt,
            r.Likes,
            likedReviewIds.Contains(r.Id), 
            r.UserId == currentUserId, // IsOwner
            r.ReviewTags.Select(rt => rt.Tag.Name).ToList(), 
            r.PingActivity.Ping.IsDeleted
        )).ToList();

        logger.LogInformation("User reviews fetched for {UserId}: {Count} reviews", targetUserId, result.Count);

        return new PaginatedResult<ExploreReviewDto>(result, count, pagination.PageNumber, pagination.PageSize);

    }

    public async Task<PaginatedResult<ExploreReviewDto>> GetFriendsFeedAsync(string userId, PaginationParams pagination)
    {
        var following = await followService.GetFollowingAsync(userId, new PaginationParams { PageNumber = 1, PageSize = 1000 });
        var friendIds = following.Items.Select(f => f.Id).ToHashSet();
        
        if (friendIds.Count == 0)
        {
            return new PaginatedResult<ExploreReviewDto>(new List<ExploreReviewDto>(), 0, pagination.PageNumber, pagination.PageSize);
        }

        var query = appDb.Reviews
            .AsNoTracking()
            .Where(r => friendIds.Contains(r.UserId))
            .Include(r => r.PingActivity)
                .ThenInclude(pa => pa.Ping)
                    .ThenInclude(p => p.PingGenre)
            .Include(r => r.ReviewTags)
                .ThenInclude(rt => rt.Tag)
            .Where(r => !r.PingActivity.Ping.IsDeleted) // Ensure ping is not soft-deleted
            .Where(r => r.PingActivity.Ping.Visibility == PingVisibility.Public || r.PingActivity.Ping.Visibility == PingVisibility.Friends)
            .OrderByDescending(r => r.CreatedAt);

        var count = await query.CountAsync();
        
        var reviews = await query
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var reviewIds = reviews.Select(r => r.Id).ToList();
        var likedReviewIds = new HashSet<int>();
        if (reviewIds.Count > 0)
        {
            likedReviewIds = await appDb.ReviewLikes
                .Where(rl => rl.UserId == userId && reviewIds.Contains(rl.ReviewId))
                .Select(rl => rl.ReviewId)
                .ToHashSetAsync();
        }

        var userIds = reviews.Select(r => r.UserId).Distinct().ToList();
        var userMap = new Dictionary<string, string?>();
        if (userIds.Count > 0)
        {
             var users = await userManager.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.ProfileImageUrl })
                .ToListAsync();
             foreach(var u in users) userMap[u.Id] = u.ProfileImageUrl;
        }

        var result = reviews.Select(r => new ExploreReviewDto(
            r.Id,
            r.PingActivityId,
            r.PingActivity.PingId,
            r.PingActivity.Ping.Name,
            r.PingActivity.Ping.Address ?? string.Empty,
            r.PingActivity.Name,
            r.PingActivity.Ping.PingGenre?.Name,
            r.PingActivity.Ping.Latitude,
            r.PingActivity.Ping.Longitude,
            r.Rating,
            r.Content,
            r.UserId,
            r.UserName,
            userMap.GetValueOrDefault(r.UserId),
            r.ImageUrl ?? "",
            r.ThumbnailUrl ?? "",
            r.CreatedAt,
            r.Likes,
            likedReviewIds.Contains(r.Id), 
            r.UserId == userId, // IsOwner
            r.ReviewTags.Select(rt => rt.Tag.Name).ToList(), 
            r.PingActivity.Ping.IsDeleted
        )).ToList();

        logger.LogInformation("Friends feed fetched for {UserId}: {Count} reviews", userId, result.Count);

        return new PaginatedResult<ExploreReviewDto>(result, count, pagination.PageNumber, pagination.PageSize);
    }

    public async Task DeleteReviewAsAdminAsync(int id)
    {
        var review = await appDb.Reviews.FindAsync(id);
        if (review != null)
        {
             appDb.Reviews.Remove(review);
             await appDb.SaveChangesAsync();
             logger.LogInformation("Review deleted by Admin: {ReviewId}", id);
        }
    }

    public async Task DeleteReviewAsync(int reviewId, string userId)
    {
        var review = await appDb.Reviews.FindAsync(reviewId);
        
        if (review == null)
        {
            throw new KeyNotFoundException("Review not found.");
        }

        if (review.UserId != userId)
        {
            throw new UnauthorizedAccessException("You are not authorized to delete this review.");
        }

        appDb.Reviews.Remove(review);
        await appDb.SaveChangesAsync();
        logger.LogInformation("Review {ReviewId} deleted by user {UserId}", reviewId, userId);
    }

    public async Task<ReviewDto> UpdateReviewAsync(int reviewId, string userId, UpdateReviewDto dto)
    {
        var review = await appDb.Reviews
            .Include(r => r.ReviewTags)
            .ThenInclude(rt => rt.Tag)
            .FirstOrDefaultAsync(r => r.Id == reviewId);

        if (review == null) throw new KeyNotFoundException("Review not found.");

        if (review.UserId != userId)
        {
            throw new UnauthorizedAccessException("You are not authorized to update this review.");
        }

        bool updated = false;

        if (dto.Rating.HasValue)
        {
            review.Rating = dto.Rating.Value;
            updated = true;
        }

        if (dto.Content != null)
        {
            if (dto.Content.Length > 1000) throw new ArgumentException("Content must be at most 1000 characters.");
            
            // Moderate content if changed
            if (dto.Content != review.Content)
            {
                 var modResult = await moderationService.CheckContentAsync(dto.Content);
                 if (modResult.IsFlagged) throw new ArgumentException($"Content rejected: {modResult.Reason}");
                 review.Content = dto.Content;
                 updated = true;
            }
        }

        if (dto.ImageUrl != null)
        {
            review.ImageUrl = dto.ImageUrl;
            review.ThumbnailUrl = !string.IsNullOrWhiteSpace(dto.ThumbnailUrl) ? dto.ThumbnailUrl : dto.ImageUrl;
            updated = true;
        }

        if (dto.Tags != null)
        {
            // Simplified Tag Update: clear existing, add new
            // A more efficient way would be diffing, but this is safer for now.
            // Actually, we must be careful not to delete global tags? No, ReviewTag is a link.
            // We delete the links (ReviewTags) not the Tags themselves.
            
            appDb.ReviewTags.RemoveRange(review.ReviewTags);
            
            var distinctTags = dto.Tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            foreach (var tagName in distinctTags)
            {
                var tag = await appDb.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
                if (tag == null)
                {
                    var tagMod = await moderationService.CheckContentAsync(tagName);
                    if (tagMod.IsFlagged) continue; 

                    tag = new Tag { Name = tagName };
                    appDb.Tags.Add(tag);
                }
                review.ReviewTags.Add(new ReviewTag { Tag = tag, ReviewId = review.Id }); // EF tracks relationship
            }
            updated = true;
        }

        if (updated)
        {
            await appDb.SaveChangesAsync();
            logger.LogInformation("Review {ReviewId} updated by {UserId}", reviewId, userId);
        }

        // Return updated DTO
        var user = await userManager.FindByIdAsync(userId);
        
        return new ReviewDto(
            review.Id,
            review.Rating,
            review.Content,
            review.UserId,
            review.UserName,
            user?.ProfileImageUrl,
            review.ImageUrl ?? "",
            review.ThumbnailUrl ?? "",
            review.CreatedAt,
            review.Likes,
            await appDb.ReviewLikes.AnyAsync(rl => rl.ReviewId == review.Id && rl.UserId == userId),
            true, // IsOwner
            review.ReviewTags.Select(rt => rt.Tag.Name).ToList(),
            review.PingActivity!.Ping.IsDeleted
        );
    }
}

