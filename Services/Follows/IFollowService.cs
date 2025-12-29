using Ping.Dtos.Common;
using Ping.Dtos.Friends; // Reusing FriendSummaryDto for now, or creating FollowerDto? Let's check Dtos.
// Actually let's reuse FriendSummaryDto but maybe alias it or creates FollowerDto if fields differ.
// FriendSummaryDto has Id, UserName, Name, Image. That's perfect for Follower.

namespace Ping.Services.Follows
{
    public interface IFollowService
    {
        Task<string> FollowUserAsync(string userId, string targetId);
        Task<string> UnfollowUserAsync(string userId, string targetId);
        Task<PaginatedResult<FriendSummaryDto>> GetFollowersAsync(string userId, PaginationParams pagination);
        Task<PaginatedResult<FriendSummaryDto>> GetFollowingAsync(string userId, PaginationParams pagination);
        Task<bool> IsFollowingAsync(string userId, string targetId);
        
        // For "My Friends" equivalent (Mutuals)
        Task<PaginatedResult<FriendSummaryDto>> GetMutualsAsync(string userId, PaginationParams pagination);
        Task<IReadOnlyList<string>> GetMutualIdsAsync(string userId);
        Task<int> GetFollowerCountAsync(string userId);
        Task<int> GetFollowingCountAsync(string userId);
    }
}
