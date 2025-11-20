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

        var review = new Review
        {
            PlaceActivityId = placeActivityId,
            UserId = userId,
            UserName = userName,
            Rating = dto.Rating,
            Content = dto.Content,
            CreatedAt = DateTime.UtcNow,
            Likes = 0,
        };

        appDb.Reviews.Add(review);
        await appDb.SaveChangesAsync();

        logger.LogInformation("Review created for Activity {PlaceActivityId} by {UserName}. Rating: {Rating}", placeActivityId, userName, dto.Rating);

        return new ReviewDto(
            review.Id,
            review.Rating,
            review.Content,
            review.UserName,
            review.CreatedAt,
            review.Likes,
            false // IsLiked - newly created review not liked yet
        );
    }

    public async Task<IEnumerable<ReviewDto>> GetReviewsAsync(int placeActivityId, string scope, string userId)
    {
        var activityExists = await appDb.PlaceActivities
            .AnyAsync(pa => pa.Id == placeActivityId);
        if (!activityExists)
            throw new KeyNotFoundException("Activity not found.");

        var query = appDb.Reviews
            .AsNoTracking()
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
                        return Array.Empty<ReviewDto>();
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

        return reviews.Select(r => new ReviewDto(
            r.Id,
            r.Rating,
            r.Content,
            r.UserName,
            r.CreatedAt,
            r.Likes,
            likedReviewIds.Contains(r.Id) // IsLiked
        )).ToList();
    }

    public async Task<IEnumerable<ExploreReviewDto>> GetExploreReviewsAsync(int pageSize, int pageNumber, string? userId)
    {
        // Validate pagination parameters
        if (pageSize <= 0 || pageSize > 100)
            pageSize = 20; // Default page size
        
        if (pageNumber < 1)
            pageNumber = 1;

        var skip = (pageNumber - 1) * pageSize;

        var reviews = await appDb.Reviews
            .AsNoTracking()
            .Include(r => r.PlaceActivity)
                .ThenInclude(pa => pa.Place)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
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
            r.PlaceActivity.Place.Latitude,
            r.PlaceActivity.Place.Longitude,
            r.Rating,
            r.Content,
            r.UserName,
            r.CreatedAt,
            r.Likes,
            likedReviewIds.Contains(r.Id) // IsLiked
        )).ToList();

        logger.LogInformation("Explore reviews fetched: Page {PageNumber}, Size {PageSize}, Count {Count}", 
            pageNumber, pageSize, result.Count);

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
            .AsNoTracking()
            .OrderByDescending(rl => rl.CreatedAt)
            .Select(rl => new ExploreReviewDto(
                rl.Review.Id,
                rl.Review.PlaceActivityId,
                rl.Review.PlaceActivity.PlaceId,
                rl.Review.PlaceActivity.Place.Name,
                rl.Review.PlaceActivity.Place.Address ?? string.Empty,
                rl.Review.PlaceActivity.Place.Latitude,
                rl.Review.PlaceActivity.Place.Longitude,
                rl.Review.Rating,
                rl.Review.Content,
                rl.Review.UserName,
                rl.Review.CreatedAt,
                rl.Review.Likes,
                true // IsLiked - always true for liked reviews
            ))
            .ToListAsync();

        logger.LogInformation("Liked reviews for {UserId} retrieved: {Count} reviews", userId, likedReviews.Count);

        return likedReviews;
    }
}
