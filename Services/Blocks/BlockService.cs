using Conquest.Data.App;
using Conquest.Models.AppUsers;
using Conquest.Models.Users;
using Microsoft.EntityFrameworkCore;

using Conquest.Data.Auth;

namespace Conquest.Services.Blocks
{
    public class BlockService(AppDbContext context, AuthDbContext authDb) : IBlockService
    {
        public async Task BlockUserAsync(string blockerId, string blockedId)
        {
            if (blockerId == blockedId)
                throw new ArgumentException("Cannot block yourself.");

            var exists = await authDb.UserBlocks
                .AnyAsync(ub => ub.BlockerId == blockerId && ub.BlockedId == blockedId);

            if (exists) return; // Already blocked

            var block = new UserBlock
            {
                BlockerId = blockerId,
                BlockedId = blockedId
            };

            authDb.UserBlocks.Add(block);
            await authDb.SaveChangesAsync();

            // Remove Friendship if exists (Bidirectional check)
            var friendships = await authDb.Friendships
                .Where(f => (f.UserId == blockerId && f.FriendId == blockedId) || 
                            (f.UserId == blockedId && f.FriendId == blockerId))
                .ToListAsync();

            if (friendships.Any())
            {
                authDb.Friendships.RemoveRange(friendships);
                await authDb.SaveChangesAsync();
            }
        }

        public async Task UnblockUserAsync(string blockerId, string blockedId)
        {
            var block = await authDb.UserBlocks
                .FirstOrDefaultAsync(ub => ub.BlockerId == blockerId && ub.BlockedId == blockedId);

            if (block == null) return;

            authDb.UserBlocks.Remove(block);
            await authDb.SaveChangesAsync();
        }

        public async Task<List<AppUser>> GetBlockedUsersAsync(string userId)
        {
            return await authDb.UserBlocks
                .Where(ub => ub.BlockerId == userId)
                .Include(ub => ub.Blocked)
                .Select(ub => ub.Blocked)
                .ToListAsync();
        }

        public async Task<bool> IsBlockedAsync(string userId, string potentiallyBlockedById)
        {
             return await authDb.UserBlocks
                .AnyAsync(ub => ub.BlockerId == potentiallyBlockedById && ub.BlockedId == userId);
        }

        public async Task<HashSet<string>> GetBlacklistedUserIdsAsync(string userId)
        {
            // Users I blocked
            var blockedByMe = await authDb.UserBlocks
                .AsNoTracking()
                .Where(ub => ub.BlockerId == userId)
                .Select(ub => ub.BlockedId)
                .ToListAsync();

            // Users who blocked me
            var blockedMe = await authDb.UserBlocks
                .AsNoTracking()
                .Where(ub => ub.BlockedId == userId)
                .Select(ub => ub.BlockerId)
                .ToListAsync();

            return blockedByMe.Concat(blockedMe).ToHashSet();
        }
    }
}
