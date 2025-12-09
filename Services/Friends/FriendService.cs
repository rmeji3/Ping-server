using Conquest.Data.Auth;
using Conquest.Dtos.Common;
using Conquest.Dtos.Friends;
using Conquest.Models.AppUsers;
using Conquest.Models.Friends;
using Conquest.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.Friends;

public class FriendService(
    AuthDbContext authDb,
    UserManager<AppUser> userManager,
    ILogger<FriendService> logger) : IFriendService
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

    public async Task<PaginatedResult<FriendSummaryDto>> GetMyFriendsAsync(string userId, PaginationParams pagination)
    {
        var query = authDb.Friendships
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.Status == Friendship.FriendshipStatus.Accepted)
            .Select(f => new FriendSummaryDto
            {
                Id = f.Friend.Id,
                UserName = f.Friend.UserName!,
                FirstName = f.Friend.FirstName,
                LastName = f.Friend.LastName,
                ProfileImageUrl = f.Friend.ProfileImageUrl
            });

        return await query.ToPaginatedResultAsync(pagination);
    }

    public async Task<string> AddFriendAsync(string userId, string friendUsername)
    {
        var friend = await userManager.FindByNameAsync(friendUsername);
        if (friend is null) throw new KeyNotFoundException("User not found.");

        // can't add yourself
        if (friend.Id == userId)
        {
            logger.LogWarning("AddFriend failed: User {UserId} tried to add themselves.", userId);
            throw new InvalidOperationException("You cannot add yourself as a friend.");
        }

        var friendId = friend.Id;

        // if we already have any Accepted link either direction, they're friends
        var alreadyFriends = await authDb.Friendships.AnyAsync(f =>
            ((f.UserId == userId && f.FriendId == friendId) ||
             (f.UserId == friendId && f.FriendId == userId)) &&
            f.Status == Friendship.FriendshipStatus.Accepted);

        if (alreadyFriends)
        {
            logger.LogWarning("AddFriend failed: {UserId} is already friends with {FriendId}", userId, friendId);
            throw new InvalidOperationException("Already friends.");
        }

        // did I already send a pending request to them?
        var existingOutgoing = await authDb.Friendships.FirstOrDefaultAsync(f =>
            f.UserId == userId &&
            f.FriendId == friendId &&
            f.Status == Friendship.FriendshipStatus.Pending);

        if (existingOutgoing is not null)
        {
            logger.LogWarning("AddFriend failed: Pending request already exists from {UserId} to {FriendId}", userId, friendId);
            throw new InvalidOperationException("You already sent a friend request to this user.");
        }

        // did THEY already send a pending request to ME?
        var existingIncoming = await authDb.Friendships.FirstOrDefaultAsync(f =>
            f.UserId == friendId &&
            f.FriendId == userId &&
            f.Status == Friendship.FriendshipStatus.Pending);

        if (existingIncoming is not null)
        {
            logger.LogWarning("AddFriend failed: Incoming request already exists from {FriendId} to {UserId}", friendId, userId);
            throw new InvalidOperationException("This user already sent you a request. Accept it instead.");
        }

        // have they blocked me?
        var blocked = await authDb.Friendships.FirstOrDefaultAsync(f =>
            f.UserId == friendId &&
            f.FriendId == userId &&
            f.Status == Friendship.FriendshipStatus.Blocked);

        if (blocked is not null)
        {
            logger.LogWarning("AddFriend failed: {UserId} is blocked by {FriendId}", userId, friendId);
            throw new InvalidOperationException("This user has blocked you. You cannot send a request.");
        }

        // create a new pending request (one row: me -> friend)
        var friendship = new Friendship
        {
            UserId = userId,
            FriendId = friendId,
            Status = Friendship.FriendshipStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        authDb.Friendships.Add(friendship);
        await authDb.SaveChangesAsync();

        logger.LogInformation("Friend request sent from {UserId} to {FriendId}", userId, friendId);
        return "Friend request sent!";
    }

    public async Task<string> AcceptFriendAsync(string userId, string friendUsername)
    {
        var friend = await userManager.FindByNameAsync(friendUsername);
        if (friend is null) throw new KeyNotFoundException("User not found.");

        var friendId = friend.Id;

        // find pending request sent by THEM to ME
        var pendingRequest = await authDb.Friendships
            .FirstOrDefaultAsync(f =>
                f.UserId == friendId &&
                f.FriendId == userId &&
                f.Status == Friendship.FriendshipStatus.Pending);

        if (pendingRequest is null)
            throw new InvalidOperationException("No pending request from this user.");

        // update the request to Accepted
        pendingRequest.Status = Friendship.FriendshipStatus.Accepted;

        // also add reverse link if it does not exist yet
        var reverseExists = await authDb.Friendships.AnyAsync(f =>
            f.UserId == userId &&
            f.FriendId == friendId &&
            f.Status == Friendship.FriendshipStatus.Accepted);

        if (!reverseExists)
        {
            authDb.Friendships.Add(new Friendship
            {
                UserId = userId,
                FriendId = friendId,
                Status = Friendship.FriendshipStatus.Accepted,
                CreatedAt = DateTime.UtcNow
            });
        }

        await authDb.SaveChangesAsync();

        logger.LogInformation("Friend request accepted: {UserId} and {FriendId}", userId, friendId);
        return "Friend request accepted.";
    }

    public async Task<PaginatedResult<FriendSummaryDto>> GetIncomingRequestsAsync(string userId, PaginationParams pagination)
    {
        var query = authDb.Friendships
            .AsNoTracking()
            .Where(f => f.FriendId == userId && f.Status == Friendship.FriendshipStatus.Pending)
            .Select(f => new FriendSummaryDto
            {
                Id = f.User.Id,
                UserName = f.User.UserName!,
                FirstName = f.User.FirstName,
                LastName = f.User.LastName
            });

        return await query.ToPaginatedResultAsync(pagination);
    }

    public async Task<string> RemoveFriendAsync(string userId, string friendUsername)
    {
        var friend = await userManager.FindByNameAsync(friendUsername);
        if (friend is null) throw new KeyNotFoundException("User not found.");

        var friendId = friend.Id;
        var friendship1 = await authDb.Friendships
            .FirstOrDefaultAsync(f => f.UserId == userId && f.FriendId == friendId && f.Status == Friendship.FriendshipStatus.Accepted);
        var friendship2 = await authDb.Friendships
            .FirstOrDefaultAsync(f => f.UserId == friendId && f.FriendId == userId && f.Status == Friendship.FriendshipStatus.Accepted);

        if (friendship1 is null && friendship2 is null)
            throw new InvalidOperationException("You are not friends with this user.");

        if (friendship1 is not null)
            authDb.Friendships.Remove(friendship1);
        if (friendship2 is not null)
            authDb.Friendships.Remove(friendship2);

        await authDb.SaveChangesAsync();
        logger.LogInformation("Friend removed: {UserId} and {FriendId}", userId, friendId);
        return "Friend removed.";
    }

    public async Task<Friendship.FriendshipStatus> GetFriendshipStatusAsync(string userId, string targetUserId)
    {
        if (userId == targetUserId) return Friendship.FriendshipStatus.Accepted; // Self is conceptually "Accepted" or handle specially.

        // Check if I sent request to them
        var outgoing = await authDb.Friendships.AsNoTracking().FirstOrDefaultAsync(f =>
            f.UserId == userId && f.FriendId == targetUserId);
        
        if (outgoing != null) return outgoing.Status;

        // Check if they sent request to me
        var incoming = await authDb.Friendships.AsNoTracking().FirstOrDefaultAsync(f =>
            f.UserId == targetUserId && f.FriendId == userId);

        if (incoming != null)
        {
            // If they blocked me, I shouldn't know? Or I should know "None"?
            // If I blocked them, I see Blocked.
            // If they sent Pending to me, I see Pending (incoming).
            return incoming.Status;
        }

        return (Friendship.FriendshipStatus)999; // None
    }
}