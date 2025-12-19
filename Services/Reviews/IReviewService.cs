using Ping.Dtos.Common;
using Ping.Dtos.Reviews;

namespace Ping.Services.Reviews;

public interface IReviewService
{
    Task<ReviewDto> CreateReviewAsync(int pingActivityId, CreateReviewDto dto, string userId, string userName);
    Task<PaginatedResult<UserReviewsDto>> GetReviewsAsync(int pingActivityId, string scope, string userId, PaginationParams pagination);
    Task<PaginatedResult<ExploreReviewDto>> GetExploreReviewsAsync(ExploreReviewsFilterDto filter, string? userId, PaginationParams pagination);
    Task LikeReviewAsync(int reviewId, string userId);
    Task UnlikeReviewAsync(int reviewId, string userId);
    Task<PaginatedResult<ExploreReviewDto>> GetLikedReviewsAsync(string userId, PaginationParams pagination);
    Task<PaginatedResult<ExploreReviewDto>> GetUserLikesAsync(string targetUserId, string viewerUserId, PaginationParams pagination);
    Task<PaginatedResult<ExploreReviewDto>> GetUserReviewsAsync(string targetUserId, string currentUserId, PaginationParams pagination);
    Task<PaginatedResult<ExploreReviewDto>> GetMyReviewsAsync(string userId, PaginationParams pagination);
    Task<PaginatedResult<ExploreReviewDto>> GetFriendsFeedAsync(string userId, PaginationParams pagination);
    Task DeleteReviewAsAdminAsync(int id);
}

