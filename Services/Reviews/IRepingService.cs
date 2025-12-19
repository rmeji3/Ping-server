using Ping.Dtos.Common;
using Ping.Dtos.Reviews;
using Ping.Models.AppUsers;

namespace Ping.Services.Reviews;

public interface IRepingService
{
    Task<RepingDto> RepostReviewAsync(int reviewId, string userId, RepostReviewDto dto);
    Task<PaginatedResult<RepingDto>> GetUserRepingsAsync(string targetUserId, string currentUserId, PaginationParams pagination);
    Task DeleteRepingAsync(int reviewId, string userId);
    Task UpdateRepingPrivacyAsync(int reviewId, string userId, PrivacyConstraint privacy);
}
