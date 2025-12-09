using Conquest.Models.AppUsers;
using Conquest.Models.Users;

namespace Conquest.Services.Blocks
{
    public interface IBlockService
    {
        Task BlockUserAsync(string blockerId, string blockedId);
        Task UnblockUserAsync(string blockerId, string blockedId);
        Task<List<AppUser>> GetBlockedUsersAsync(string userId);
        Task<bool> IsBlockedAsync(string userId, string potentiallyBlockedById);
        Task<HashSet<string>> GetBlacklistedUserIdsAsync(string userId);
    }
}
