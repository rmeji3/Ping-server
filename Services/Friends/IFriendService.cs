using Conquest.Dtos.Common;
using Conquest.Dtos.Friends;
using Conquest.Models.Friends;

namespace Conquest.Services.Friends;

public interface IFriendService
{
    Task<IReadOnlyList<string>> GetFriendIdsAsync(string userId);
    Task<PaginatedResult<FriendSummaryDto>> GetMyFriendsAsync(string userId, PaginationParams pagination);
    Task<string> AddFriendAsync(string userId, string friendUsername);
    Task<string> AcceptFriendAsync(string userId, string friendUsername);
    Task<PaginatedResult<FriendSummaryDto>> GetIncomingRequestsAsync(string userId, PaginationParams pagination);
    Task<string> RemoveFriendAsync(string userId, string friendUsername);
    Task<Friendship.FriendshipStatus> GetFriendshipStatusAsync(string userId, string targetUserId);
}