using Conquest.Models.Friends;
using Microsoft.EntityFrameworkCore;

namespace Conquest.Services.Friends;
using Data.Auth;

public class FriendService(AuthDbContext authDb) : IFriendService
{
    public async Task<IReadOnlyList<string>> GetFriendIdsAsync(string userId)
    {
        return await authDb.Friendships
            .Where(f => 
                f.UserId == userId && f.Status == Friendship.FriendshipStatus.Accepted ||
                f.FriendId == userId && f.Status == Friendship.FriendshipStatus.Accepted)
            .Select(f => f.UserId == userId ? f.FriendId : f.UserId)
            .ToListAsync();
    }
}