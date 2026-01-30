using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Ping.Data.App;
using Ping.Dtos.Common;
using Ping.Dtos.Reviews;
using Ping.Models.AppUsers;
using Ping.Models.Reviews;
using Ping.Services.Follows;
using Ping.Services.Blocks;
using Ping.Models.Pings;

namespace Ping.Services.Reviews;

public class RepingService(
    AppDbContext appDb,
    IFollowService followService,
    IBlockService blockService,
    UserManager<AppUser> userManager,
    ILogger<RepingService> logger) : IRepingService
{
    public async Task<RepingDto> RepostReviewAsync(int reviewId, string userId, RepostReviewDto dto)
    {
        logger.LogInformation("User {UserId} starting repost of review {ReviewId}", userId, reviewId);
        
        var review = await appDb.Reviews
            .AsNoTracking()
            .Include(r => r.PingActivity)
            .ThenInclude(pa => pa.Ping)
            .FirstOrDefaultAsync(r => r.Id == reviewId);

        if (review == null) 
        {
            logger.LogWarning("RepostReview: Review {ReviewId} not found for user {UserId}", reviewId, userId);
            throw new KeyNotFoundException("Review not found.");
        }

        // Check if already repinged
        var existing = await appDb.Repings
            .FirstOrDefaultAsync(r => r.ReviewId == reviewId && r.UserId == userId);
        
        if (existing != null)
        {
             logger.LogWarning("RepostReview: User {UserId} already repinged review {ReviewId}", userId, reviewId);
             throw new InvalidOperationException("Review already repinged.");
        }

        var reping = new Reping
        {
            ReviewId = reviewId,
            UserId = userId,
            Privacy = dto.Privacy,
            CreatedAt = DateTime.UtcNow
        };

        appDb.Repings.Add(reping);
        await appDb.SaveChangesAsync();
        
        logger.LogInformation("RepostReview: Success. User {UserId} repinged review {ReviewId}. Privacy: {Privacy}", userId, reviewId, dto.Privacy);

        return await MapToDtoAsync(reping, userId);
    }

    public async Task<PaginatedResult<RepingDto>> GetUserRepingsAsync(string targetUserId, string currentUserId, PaginationParams pagination)
    {
        var targetUser = await userManager.FindByIdAsync(targetUserId);
        if (targetUser == null) throw new KeyNotFoundException("User not found.");

        bool isSelf = targetUserId == currentUserId;
        bool isFriend = false;
        
        if (!isSelf)
        {
            var isBlocked = await blockService.IsBlockedAsync(currentUserId, targetUserId) ||
                            await blockService.IsBlockedAsync(targetUserId, currentUserId);
            if (isBlocked) throw new KeyNotFoundException("User not found.");

            var isFollowing = await followService.IsFollowingAsync(currentUserId, targetUserId);
            var isFollowedBy = await followService.IsFollowingAsync(targetUserId, currentUserId);
            isFriend = isFollowing && isFollowedBy;
        }

        logger.LogInformation("GetUserRepings: Fetching repings for target {TargetUser} by viewer {ViewerUser}. Friend: {IsFriend}", targetUserId, currentUserId, isFriend);

        var query = appDb.Repings
            .AsNoTracking()
            .Where(r => r.UserId == targetUserId)
            .Include(r => r.Review)
                .ThenInclude(rev => rev.PingActivity)
                    .ThenInclude(pa => pa.Ping)
                        .ThenInclude(p => p.PingGenre)
            .Include(r => r.Review)
                .ThenInclude(rev => rev.ReviewTags)
                    .ThenInclude(rt => rt.Tag)
            .Where(r => !r.Review.PingActivity.Ping.IsDeleted);

        // Privacy Filter on Repings
        if (!isSelf)
        {
            query = query.Where(r => 
                r.Privacy == PrivacyConstraint.Public ||
                (r.Privacy == PrivacyConstraint.FriendsOnly && isFriend)
            );
            
            // Further filter by Review/Ping Visibility
            query = query.Where(r => 
                r.Review.PingActivity.Ping.Visibility == PingVisibility.Public ||
                r.Review.PingActivity.Ping.OwnerUserId == currentUserId
            );
        }

        query = query.OrderByDescending(r => r.CreatedAt);

        var count = await query.CountAsync();
        var items = await query
            .Skip((pagination.PageNumber - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync();

        var dtos = new List<RepingDto>();
        
        // Batch fetch user data for Mapping
        var reviewerIds = items.Select(r => r.Review.UserId).Distinct().ToList();
        var userMap = await userManager.Users.AsNoTracking()
            .Where(u => reviewerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.ProfileImageUrl);

        // Batch fetch likes for the reviews
        var reviewIds = items.Select(r => r.ReviewId).ToList();
        var likedReviewIds = new HashSet<int>();
        if (currentUserId != null)
        {
            likedReviewIds = await appDb.ReviewLikes
                .Where(rl => rl.UserId == currentUserId && reviewIds.Contains(rl.ReviewId))
                .Select(rl => rl.ReviewId)
                .ToHashSetAsync();
        }

        foreach (var item in items)
        {
             var reviewDto = new ExploreReviewDto(
                item.Review.Id,
                item.Review.PingActivityId,
                item.Review.PingActivity.PingId,
                item.Review.PingActivity.Ping.Name,
                item.Review.PingActivity.Ping.Address ?? string.Empty,
                item.Review.PingActivity.Name,
                item.Review.PingActivity.Ping.PingGenre?.Name,
                item.Review.PingActivity.Ping.Latitude,
                item.Review.PingActivity.Ping.Longitude,
                item.Review.Rating,
                item.Review.Content,
                item.Review.UserId,
                item.Review.UserName,
                userMap.GetValueOrDefault(item.Review.UserId),
                item.Review.ImageUrl,
                item.Review.ThumbnailUrl,
                item.Review.CreatedAt,
                item.Review.Likes,
                likedReviewIds.Contains(item.ReviewId),
                item.Review.UserId == currentUserId, // IsOwner
                item.Review.ReviewTags.Select(rt => rt.Tag.Name).ToList(),
                item.Review.PingActivity.Ping.IsDeleted
            );

            dtos.Add(new RepingDto(
                item.Id,
                item.ReviewId,
                item.UserId,
                item.CreatedAt,
                item.Privacy,
                reviewDto
            ));
        }

        logger.LogInformation("GetUserRepings: Completed. User {TargetUserId} has {Count} visible repings for viewer {CurrentUserId}", targetUserId, count, currentUserId);

        return new PaginatedResult<RepingDto>(dtos, count, pagination.PageNumber, pagination.PageSize);
    }

    public async Task DeleteRepingAsync(int reviewId, string userId)
    {
        logger.LogInformation("User {UserId} removing reping for review {ReviewId}", userId, reviewId);

        var reping = await appDb.Repings
            .FirstOrDefaultAsync(r => r.ReviewId == reviewId && r.UserId == userId);
        
        if (reping == null) 
        {
            logger.LogWarning("DeleteReping: Reping not found for Review {ReviewId} and User {UserId}", reviewId, userId);
            throw new KeyNotFoundException("Reping not found.");
        }

        appDb.Repings.Remove(reping);
        await appDb.SaveChangesAsync();
        logger.LogInformation("DeleteReping: Success. User {UserId} removed reping for review {ReviewId}", userId, reviewId);
    }

    public async Task UpdateRepingPrivacyAsync(int reviewId, string userId, PrivacyConstraint privacy)
    {
        logger.LogInformation("User {UserId} updating reping privacy for review {ReviewId} to {Privacy}", userId, reviewId, privacy);

        var reping = await appDb.Repings
            .FirstOrDefaultAsync(r => r.ReviewId == reviewId && r.UserId == userId);
        
        if (reping == null) 
        {
            logger.LogWarning("UpdateRepingPrivacy: Reping not found for Review {ReviewId} and User {UserId}", reviewId, userId);
            throw new KeyNotFoundException("Reping not found.");
        }

        reping.Privacy = privacy;
        await appDb.SaveChangesAsync();
        logger.LogInformation("UpdateRepingPrivacy: Success. User {UserId} updated privacy for {ReviewId}", userId, reviewId);
    }

    private async Task<RepingDto> MapToDtoAsync(Reping reping, string currentUserId)
    {
        // Fetch review details for mapping
        var item = await appDb.Repings
            .AsNoTracking()
            .Include(r => r.Review)
                .ThenInclude(rev => rev.PingActivity)
                    .ThenInclude(pa => pa.Ping)
                        .ThenInclude(p => p.PingGenre)
            .Include(r => r.Review)
                .ThenInclude(rev => rev.ReviewTags)
                    .ThenInclude(rt => rt.Tag)
            .FirstOrDefaultAsync(r => r.Id == reping.Id);

        if (item == null) throw new KeyNotFoundException("Reping not found.");

        var reviewer = await userManager.FindByIdAsync(item.Review.UserId);
        var isLiked = await appDb.ReviewLikes.AnyAsync(rl => rl.UserId == currentUserId && rl.ReviewId == item.ReviewId);

        var reviewDto = new ExploreReviewDto(
            item.Review.Id,
            item.Review.PingActivityId,
            item.Review.PingActivity.PingId,
            item.Review.PingActivity.Ping.Name,
            item.Review.PingActivity.Ping.Address ?? string.Empty,
            item.Review.PingActivity.Name,
            item.Review.PingActivity.Ping.PingGenre?.Name,
            item.Review.PingActivity.Ping.Latitude,
            item.Review.PingActivity.Ping.Longitude,
            item.Review.Rating,
            item.Review.Content,
            item.Review.UserId,
            item.Review.UserName,
            reviewer?.ProfileImageUrl,
            item.Review.ImageUrl,
            item.Review.ThumbnailUrl,
            item.Review.CreatedAt,
            item.Review.Likes,
            isLiked,
            item.Review.UserId == currentUserId, // IsOwner
            item.Review.ReviewTags.Select(rt => rt.Tag.Name).ToList(),
            item.Review.PingActivity.Ping.IsDeleted
        );

        return new RepingDto(
            item.Id,
            item.ReviewId,
            item.UserId,
            item.CreatedAt,
            item.Privacy,
            reviewDto
        );
    }
}
