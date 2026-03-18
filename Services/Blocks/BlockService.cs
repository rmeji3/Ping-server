using Ping.Data.App;
using Ping.Models.AppUsers;
using Ping.Models.Users;
using Microsoft.EntityFrameworkCore;

using Ping.Data.Auth;

namespace Ping.Services.Blocks
{
    public class BlockService(AuthDbContext authDb) : IBlockService
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

            // Remove Follows if exists (Bidirectional check)
            var follows = await authDb.Follows
                .Where(f => (f.FollowerId == blockerId && f.FolloweeId == blockedId) || 
                            (f.FollowerId == blockedId && f.FolloweeId == blockerId))
                .ToListAsync();

            if (follows.Any())
            {
                authDb.Follows.RemoveRange(follows);
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

        public async Task<List<Ping.Dtos.Friends.BlockDto>> GetBlockedUsersAsync(string userId)
        {
            return await authDb.UserBlocks
                .Where(ub => ub.BlockerId == userId)
                .Include(ub => ub.Blocked)
                .Select(ub => new Ping.Dtos.Friends.BlockDto(
                    ub.BlockedId,
                    ub.Blocked.UserName!,
                    ub.Blocked.ProfileImageUrl,
                    ub.CreatedAt
                ))
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

