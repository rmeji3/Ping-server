using Conquest.Dtos.Common;
using Conquest.Dtos.Reviews;

namespace Conquest.Services.Reviews;

public interface IReviewService
{
    Task<ReviewDto> CreateReviewAsync(int placeActivityId, CreateReviewDto dto, string userId, string userName);
    Task<PaginatedResult<UserReviewsDto>> GetReviewsAsync(int placeActivityId, string scope, string userId, PaginationParams pagination);
    Task<PaginatedResult<ExploreReviewDto>> GetExploreReviewsAsync(ExploreReviewsFilterDto filter, string? userId, PaginationParams pagination);
    Task LikeReviewAsync(int reviewId, string userId);
    Task UnlikeReviewAsync(int reviewId, string userId);
    Task<PaginatedResult<ExploreReviewDto>> GetLikedReviewsAsync(string userId, PaginationParams pagination);
    Task<PaginatedResult<ExploreReviewDto>> GetUserReviewsAsync(string targetUserId, string currentUserId, PaginationParams pagination);
    Task<PaginatedResult<ExploreReviewDto>> GetMyReviewsAsync(string userId, PaginationParams pagination);
}
