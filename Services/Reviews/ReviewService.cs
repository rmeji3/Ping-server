using Conquest.Data.App;
using Conquest.Dtos.Reviews;
using Conquest.Models.Reviews;
using Conquest.Services.Friends;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.Reviews;

public class ReviewService(
    AppDbContext appDb,
    IFriendService friendService,
    Conquest.Services.Moderation.IModerationService moderationService,
    ILogger<ReviewService> logger) : IReviewService
{
    public async Task<ReviewDto> CreateReviewAsync(int placeActivityId, CreateReviewDto dto, string userId, string userName)
    {
        // Ensure activity exists
        var activityExists = await appDb.PlaceActivities
            .AnyAsync(pa => pa.Id == placeActivityId);

        if (!activityExists)
        {
            logger.LogWarning("CreateReview: Activity {PlaceActivityId} not found.", placeActivityId);
            throw new KeyNotFoundException("Activity not found.");
        }

        // Check if user already has a review for this activity
        var hasReview = await appDb.Reviews
            .AnyAsync(r => r.PlaceActivityId == placeActivityId && r.UserId == userId);

        if (dto.Rating < 1 || dto.Rating > 5)
        {
            logger.LogWarning("CreateReview: Invalid rating {Rating} for activity {PlaceActivityId} by {UserName}", dto.Rating, placeActivityId, userName);
            throw new ArgumentException("Rating must be between 1 and 5.");
        }

        if (dto.Content?.Length > 1000)
        {
            logger.LogWarning("CreateReview: Content too long for activity {PlaceActivityId} by {UserName}", placeActivityId, userName);
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
            PlaceActivityId = placeActivityId,
            UserId = userId,
            UserName = userName,
            Rating = dto.Rating,
            Type = hasReview ? ReviewType.CheckIn : ReviewType.Review,
            Content = dto.Content,
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
                    // Save immediately to get Id? Or just rely on EF tracking?
                    // EF tracking handles it if we add to context.
                }

                review.ReviewTags.Add(new ReviewTag { Tag = tag });
            }
        }

        appDb.Reviews.Add(review);
        await appDb.SaveChangesAsync();

        logger.LogInformation("Review created for Activity {PlaceActivityId} by {UserName}. Rating: {Rating}", placeActivityId, userName, dto.Rating);

        return new ReviewDto(
            review.Id,
            review.Rating,
            review.Content,
            review.UserId,
            review.UserName,
            review.CreatedAt,
            review.Likes,
            false, // IsLiked
            dto.Tags ?? new List<string>() // Return tags
        );
    }

    public async Task<IEnumerable<UserReviewsDto>> GetReviewsAsync(int placeActivityId, string scope, string userId)
    {
        var activityExists = await appDb.PlaceActivities
            .AnyAsync(pa => pa.Id == placeActivityId);
        if (!activityExists)
            throw new KeyNotFoundException("Activity not found.");

        var query = appDb.Reviews
            .AsNoTracking()
            .Include(r => r.ReviewTags)
                .ThenInclude(rt => rt.Tag)
            .Where(r => r.PlaceActivityId == placeActivityId)
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();

        switch (scope.ToLowerInvariant())
        {
            case "mine":
                {
                    query = query.Where(r => r.UserId == userId);
                    break;
                }
            case "friends":
                {
                    var friendIds = await friendService.GetFriendIdsAsync(userId);
                    if (friendIds.Count == 0)
                    {
                        // no friends → no reviews in this scope
                        return Array.Empty<UserReviewsDto>();
                    }
                    query = query.Where(r => friendIds.Contains(r.UserId));
                    break;
                }
            case "global":
            default:
                // no extra filter
                break;
        }

        var reviews = await query.ToListAsync();

        // Batch check which reviews are liked by the current user
        var reviewIds = reviews.Select(r => r.Id).ToList();
        var likedReviewIds = new HashSet<int>();
        
        if (!string.IsNullOrEmpty(userId))
        {
            likedReviewIds = await appDb.ReviewLikes
                .Where(rl => rl.UserId == userId && reviewIds.Contains(rl.ReviewId))
                .Select(rl => rl.ReviewId)
                .ToHashSetAsync();
        }

        var reviewDtos = reviews.Select(r => new ReviewDto(
            r.Id,
            r.Rating,
            r.Content,
            r.UserId,
            r.UserName,
            r.CreatedAt,
            r.Likes,
            likedReviewIds.Contains(r.Id), // IsLiked
            r.ReviewTags.Select(rt => rt.Tag.Name).ToList() // Tags
        )).ToList();

        // Group by UserId
        var groupedReviews = reviewDtos
            .GroupBy(r => r.UserId)
            .Select(g =>
            {
                var userReviews = g.OrderByDescending(r => r.CreatedAt).ToList();
                var latest = userReviews.First();
                var history = userReviews.Skip(1).ToList();
                return new UserReviewsDto(latest, history);
            })
            .ToList();

        return groupedReviews;
    }

    public async Task<IEnumerable<ExploreReviewDto>> GetExploreReviewsAsync(ExploreReviewsFilterDto filter, string? userId)
    {
        // Validate pagination parameters
        if (filter.PageSize <= 0 || filter.PageSize > 100)
            filter.PageSize = 20;
        
        if (filter.PageNumber < 1)
            filter.PageNumber = 1;

        var query = appDb.Reviews
            .AsNoTracking()
            .Include(r => r.PlaceActivity)
                .ThenInclude(pa => pa.Place)
            .Include(r => r.PlaceActivity)
                .ThenInclude(pa => pa.ActivityKind)
            .Include(r => r.ReviewTags)
                .ThenInclude(rt => rt.Tag)
            .Where(r => !r.PlaceActivity.Place.IsDeleted)
            .AsQueryable();

        // Filter by Category
        if (filter.ActivityKindIds != null && filter.ActivityKindIds.Any())
        {
            query = query.Where(r => r.PlaceActivity.ActivityKindId.HasValue &&
                                     filter.ActivityKindIds.Contains(r.PlaceActivity.ActivityKindId.Value));
        }

        // Filter by Search Query
        if (!string.IsNullOrWhiteSpace(filter.SearchQuery))
        {
            var q = filter.SearchQuery.ToLower();
            query = query.Where(r => r.PlaceActivity.Place.Name.ToLower().Contains(q) ||
                                     (r.PlaceActivity.Place.Address != null && r.PlaceActivity.Place.Address.ToLower().Contains(q)));
        }

        // Filter by Location (Bounding Box)
        if (filter.Latitude.HasValue && filter.Longitude.HasValue && filter.RadiusKm.HasValue)
        {
            var lat = filter.Latitude.Value;
            var lon = filter.Longitude.Value;
            var radius = filter.RadiusKm.Value;
            
            // 1 deg lat ~= 111 km
            var latDelta = radius / 111.0;
            // 1 deg lon ~= 111 km * cos(lat)
            var cosLat = Math.Cos(lat * (Math.PI / 180.0));
            var lonDelta = radius / (111.0 * (Math.Abs(cosLat) < 0.0001 ? 1 : cosLat));

            var minLat = lat - latDelta;
            var maxLat = lat + latDelta;
            var minLon = lon - lonDelta;
            var maxLon = lon + lonDelta;

            query = query.Where(r => r.PlaceActivity.Place.Latitude >= minLat && r.PlaceActivity.Place.Latitude <= maxLat &&
                                     r.PlaceActivity.Place.Longitude >= minLon && r.PlaceActivity.Place.Longitude <= maxLon);
        }

        // Sort by Likes (Default)
        query = query.OrderByDescending(r => r.Likes);

        var skip = (filter.PageNumber - 1) * filter.PageSize;

        var reviews = await query
            .Skip(skip)
            .Take(filter.PageSize)
            .ToListAsync();

        // Batch check which reviews are liked by the current user
        var reviewIds = reviews.Select(r => r.Id).ToList();
        var likedReviewIds = new HashSet<int>();
        
        if (userId != null)
        {
            likedReviewIds = await appDb.ReviewLikes
                .Where(rl => rl.UserId == userId && reviewIds.Contains(rl.ReviewId))
                .Select(rl => rl.ReviewId)
                .ToHashSetAsync();
        }

        var result = reviews.Select(r => new ExploreReviewDto(
            r.Id,
            r.PlaceActivityId,
            r.PlaceActivity.PlaceId,
            r.PlaceActivity.Place.Name,
            r.PlaceActivity.Place.Address ?? string.Empty,
            r.PlaceActivity.Name,
            r.PlaceActivity.ActivityKind?.Name,
            r.PlaceActivity.Place.Latitude,
            r.PlaceActivity.Place.Longitude,
            r.Rating,
            r.Content,
            r.UserName,
            r.CreatedAt,
            r.Likes,
            likedReviewIds.Contains(r.Id), // IsLiked
            r.ReviewTags.Select(rt => rt.Tag.Name).ToList(), // Tags
            r.PlaceActivity.Place.IsDeleted
        )).ToList();

        logger.LogInformation("Explore reviews fetched: Page {PageNumber}, Size {PageSize}, Count {Count}", 
            filter.PageNumber, filter.PageSize, result.Count);

        return result;
    }

    public async Task LikeReviewAsync(int reviewId, string userId)
    {
        // Check if review exists
        var reviewExists = await appDb.Reviews.AnyAsync(r => r.Id == reviewId);
        if (!reviewExists)
        {
            throw new KeyNotFoundException("Review not found.");
        }

        // Check if already liked
        var existingLike = await appDb.ReviewLikes
            .FirstOrDefaultAsync(rl => rl.ReviewId == reviewId && rl.UserId == userId);

        if (existingLike != null)
        {
            logger.LogInformation("Review {ReviewId} already liked by {UserId}", reviewId, userId);
            return; // Idempotent
        }

        // Add like
        var like = new ReviewLike
        {
            ReviewId = reviewId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        appDb.ReviewLikes.Add(like);

        // Increment like count
        var review = await appDb.Reviews.FindAsync(reviewId);
        if (review != null)
        {
            review.Likes++;
        }

        await appDb.SaveChangesAsync();
        logger.LogInformation("Review {ReviewId} liked by {UserId}", reviewId, userId);
    }

    public async Task UnlikeReviewAsync(int reviewId, string userId)
    {
        var like = await appDb.ReviewLikes
            .FirstOrDefaultAsync(rl => rl.ReviewId == reviewId && rl.UserId == userId);

        if (like == null)
        {
            logger.LogInformation("Review {ReviewId} not liked by {UserId}, nothing to unlike", reviewId, userId);
            return; // Idempotent
        }

        appDb.ReviewLikes.Remove(like);

        // Decrement like count
        var review = await appDb.Reviews.FindAsync(reviewId);
        if (review != null && review.Likes > 0)
        {
            review.Likes--;
        }

        await appDb.SaveChangesAsync();
        logger.LogInformation("Review {ReviewId} unliked by {UserId}", reviewId, userId);
    }

    public async Task<IEnumerable<ExploreReviewDto>> GetLikedReviewsAsync(string userId)
    {
        var likedReviews = await appDb.ReviewLikes
            .Where(rl => rl.UserId == userId)
            .Include(rl => rl.Review)
                .ThenInclude(r => r.PlaceActivity)
                    .ThenInclude(pa => pa.Place)
            .Include(rl => rl.Review)
                .ThenInclude(r => r.PlaceActivity)
                    .ThenInclude(pa => pa.ActivityKind)
            .Include(rl => rl.Review)
                .ThenInclude(r => r.ReviewTags)
                    .ThenInclude(rt => rt.Tag)
            .AsNoTracking()
            .OrderByDescending(rl => rl.CreatedAt)
            .Select(rl => new ExploreReviewDto(
                rl.Review.Id,
                rl.Review.PlaceActivityId,
                rl.Review.PlaceActivity.PlaceId,
                rl.Review.PlaceActivity.Place.Name,
                rl.Review.PlaceActivity.Place.Address ?? string.Empty,
                rl.Review.PlaceActivity.Name,
                rl.Review.PlaceActivity.ActivityKind != null ? rl.Review.PlaceActivity.ActivityKind.Name : null,
                rl.Review.PlaceActivity.Place.Latitude,
                rl.Review.PlaceActivity.Place.Longitude,
                rl.Review.Rating,
                rl.Review.Content,
                rl.Review.UserName,
                rl.Review.CreatedAt,
                rl.Review.Likes,
                true, // IsLiked - always true for liked reviews
                rl.Review.ReviewTags.Select(rt => rt.Tag.Name).ToList(), // Tags
                rl.Review.PlaceActivity.Place.IsDeleted
            ))
            .ToListAsync();

        logger.LogInformation("Liked reviews for {UserId} retrieved: {Count} reviews", userId, likedReviews.Count);

        return likedReviews;
    }
    public async Task<IEnumerable<ExploreReviewDto>> GetMyReviewsAsync(string userId)
    {
        var myReviews = await appDb.Reviews
            .Where(r => r.UserId == userId)
            .Include(r => r.PlaceActivity)
                .ThenInclude(pa => pa.Place)
            .Include(r => r.PlaceActivity)
                .ThenInclude(pa => pa.ActivityKind)
            .Include(r => r.ReviewTags)
                .ThenInclude(rt => rt.Tag)
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        // Batch check which reviews are liked by the current user (my own reviews can also be liked by me)
        var reviewIds = myReviews.Select(r => r.Id).ToList();
        var likedReviewIds = new HashSet<int>();
        
        if (reviewIds.Count > 0)
        {
            likedReviewIds = await appDb.ReviewLikes
                .Where(rl => rl.UserId == userId && reviewIds.Contains(rl.ReviewId))
                .Select(rl => rl.ReviewId)
                .ToHashSetAsync();
        }

        var result = myReviews.Select(r => new ExploreReviewDto(
            r.Id,
            r.PlaceActivityId,
            r.PlaceActivity.PlaceId,
            r.PlaceActivity.Place.Name,
            r.PlaceActivity.Place.Address ?? string.Empty,
            r.PlaceActivity.Name,
            r.PlaceActivity.ActivityKind?.Name,
            r.PlaceActivity.Place.Latitude,
            r.PlaceActivity.Place.Longitude,
            r.Rating,
            r.Content,
            r.UserName,
            r.CreatedAt,
            r.Likes,
            likedReviewIds.Contains(r.Id), // IsLiked
            r.ReviewTags.Select(rt => rt.Tag.Name).ToList(), // Tags
            r.PlaceActivity.Place.IsDeleted
        )).ToList();

        logger.LogInformation("My reviews fetched for {UserId}: {Count} reviews", userId, result.Count);

        return result;
    }
}
