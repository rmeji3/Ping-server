using Ping.Data.Auth;
using Ping.Dtos.Common;
using Ping.Dtos.Friends;
using Ping.Models;
using Ping.Models.AppUsers;
using Ping.Models.Follows;
using Ping.Services.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ping.Utils;

namespace Ping.Services.Follows;

public class FollowService(
    AuthDbContext authDb,
    UserManager<AppUser> userManager,
    INotificationService notificationService,
    Services.Blocks.IBlockService blockService,
    ILogger<FollowService> logger) : IFollowService
{
    public async Task<string> FollowUserAsync(string userId, string targetId)
    {
        if (userId == targetId) throw new InvalidOperationException("Cannot follow yourself.");

        var targetUser = await userManager.FindByIdAsync(targetId);
        if (targetUser is null) throw new KeyNotFoundException("User not found.");

        // Check block
        if (await blockService.IsBlockedAsync(targetId, userId)) // Target blocked Me
            throw new InvalidOperationException("You cannot follow this user."); 
        
        if (await blockService.IsBlockedAsync(userId, targetId)) // I blocked Target
            throw new InvalidOperationException("Unblock this user to follow them.");

        var existing = await authDb.Follows.FindAsync(userId, targetId);
        if (existing != null) return "Already following.";

        var follow = new Follow
        {
            FollowerId = userId,
            FolloweeId = targetId
        };

        authDb.Follows.Add(follow);
        await authDb.SaveChangesAsync();

        // Notification
        var me = await userManager.FindByIdAsync(userId);
        await notificationService.SendNotificationAsync(new Notification
        {
            UserId = targetId,
            SenderId = userId,
            SenderName = me?.UserName ?? "Someone",
            SenderProfileImageUrl = me?.ProfileImageUrl,
            Type = NotificationType.NewFollower,
            Title = "New Follower",
            Message = $"{me?.UserName ?? "Someone"} started following you.",
            ReferenceId = userId
        });
        
        logger.LogInformation("User {UserId} followed {TargetId}", userId, targetId);

        return "Followed successfully.";
    }

    public async Task<string> UnfollowUserAsync(string userId, string targetId)
    {
        var existing = await authDb.Follows.FindAsync(userId, targetId);
        if (existing == null) throw new InvalidOperationException("Not following.");

        authDb.Follows.Remove(existing);
        await authDb.SaveChangesAsync();

        return "Unfollowed successfully.";
    }

    public async Task<PaginatedResult<FriendSummaryDto>> GetFollowersAsync(string userId, PaginationParams pagination)
    {
        var query = authDb.Follows
            .AsNoTracking()
            .Where(f => f.FolloweeId == userId)
            .OrderByDescending(f => f.CreatedAt) // Newest followers first
            .Select(f => new FriendSummaryDto
            {
                Id = f.Follower.Id,
                UserName = f.Follower.UserName!,
                FirstName = f.Follower.FirstName,
                LastName = f.Follower.LastName,
                ProfileImageUrl = f.Follower.ProfileImageUrl
            });

        return await query.ToPaginatedResultAsync(pagination);
    }

    public async Task<PaginatedResult<FriendSummaryDto>> GetFollowingAsync(string userId, PaginationParams pagination)
    {
        var query = authDb.Follows
            .AsNoTracking()
            .Where(f => f.FollowerId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FriendSummaryDto
            {
                Id = f.Followee.Id,
                UserName = f.Followee.UserName!,
                FirstName = f.Followee.FirstName,
                LastName = f.Followee.LastName,
                ProfileImageUrl = f.Followee.ProfileImageUrl
            });

        return await query.ToPaginatedResultAsync(pagination);
    }

    public async Task<bool> IsFollowingAsync(string userId, string targetId)
    {
        return await authDb.Follows.AnyAsync(f => f.FollowerId == userId && f.FolloweeId == targetId);
    }

    public async Task<PaginatedResult<FriendSummaryDto>> GetMutualsAsync(string userId, PaginationParams pagination)
    {
        // Mutuals: Users I follow AND who follow me.
        // I follow them: Follows(Me, Them)
        // They follow me: Follows(Them, Me)

        var query = authDb.Follows
            .AsNoTracking()
            .Where(f => f.FollowerId == userId) // I follow them
            .Where(f => authDb.Follows.Any(f2 => f2.FollowerId == f.FolloweeId && f2.FolloweeId == userId)) // They follow me
            .Select(f => new FriendSummaryDto
            {
                Id = f.Followee.Id,
                UserName = f.Followee.UserName!,
                FirstName = f.Followee.FirstName,
                LastName = f.Followee.LastName,
                ProfileImageUrl = f.Followee.ProfileImageUrl
            });

        return await query.ToPaginatedResultAsync(pagination);
    }

    public async Task<IReadOnlyList<string>> GetMutualIdsAsync(string userId)
    {
        return await authDb.Follows
            .AsNoTracking()
            .Where(f => f.FollowerId == userId)
            .Where(f => authDb.Follows.Any(f2 => f2.FollowerId == f.FolloweeId && f2.FolloweeId == userId))
            .Select(f => f.FolloweeId)
            .ToListAsync();
    }
    public async Task<int> GetFollowerCountAsync(string userId)
    {
        return await authDb.Follows.CountAsync(f => f.FolloweeId == userId);
    }

    public async Task<int> GetFollowingCountAsync(string userId)
    {
        return await authDb.Follows.CountAsync(f => f.FollowerId == userId);
    }
}
