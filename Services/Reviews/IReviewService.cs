using Conquest.Dtos.Reviews;

namespace Conquest.Services.Reviews;

public interface IReviewService
{
    Task<ReviewDto> CreateReviewAsync(int placeActivityId, CreateReviewDto dto, string userId, string userName);
    Task<IEnumerable<UserReviewsDto>> GetReviewsAsync(int placeActivityId, string scope, string userId);
    Task<IEnumerable<ExploreReviewDto>> GetExploreReviewsAsync(ExploreReviewsFilterDto filter, string? userId);
    Task LikeReviewAsync(int reviewId, string userId);
    Task UnlikeReviewAsync(int reviewId, string userId);
    Task<IEnumerable<ExploreReviewDto>> GetLikedReviewsAsync(string userId);
}
