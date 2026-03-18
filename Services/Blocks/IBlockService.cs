using Ping.Models.AppUsers;
using Ping.Models.Users;

namespace Ping.Services.Blocks
{
    public interface IBlockService
    {
        Task BlockUserAsync(string blockerId, string blockedId);
        Task UnblockUserAsync(string blockerId, string blockedId);
        Task<List<Ping.Dtos.Friends.BlockDto>> GetBlockedUsersAsync(string userId);
        Task<bool> IsBlockedAsync(string userId, string potentiallyBlockedById);
        Task<HashSet<string>> GetBlacklistedUserIdsAsync(string userId);
    }
}

