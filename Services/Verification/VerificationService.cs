using Microsoft.EntityFrameworkCore;
using Ping.Data.App;
using Ping.Dtos.Verification;
using Ping.Dtos.Common;
using Ping.Services.Follows;
using Ping.Models.Users;
using Ping.Models.AppUsers;

namespace Ping.Services.Verification
{
    public class VerificationService(AppDbContext context, IFollowService followService, Microsoft.AspNetCore.Identity.UserManager<AppUser> userManager) : IVerificationService
    {
        public async Task ApplyAsync(string userId)
        {
            // 1. Check if user already verified
            var user = await userManager.FindByIdAsync(userId) ?? throw new KeyNotFoundException("User not found.");
            if (user.IsVerified)
                throw new InvalidOperationException("User is already verified.");

            // 2. Check for pending request
            var pending = await context.VerificationRequests
                .AnyAsync(r => r.UserId == userId && r.Status == VerificationStatus.Pending);
            if (pending)
                throw new InvalidOperationException("Verification request already pending.");

            // 3. Check followers count
            // We use GetFollowersAsync for now, which returns paginated.
            // Ideally IFollowService should have GetFollowerCountAsync.
            // For now, let's assume we can get a list or count.
            // Checking the interface provided earlier... it only has GetFollowersAsync.
            // I should assume the user might have > 500 followers.
            // Querying all might be expensive if they have 500k. 
            // Better to add Count method to FollowService later?
            // For now, let's try to get the first page with a limit 1, but we actually need the *total* count.
            // PaginatedResult usually has TotalCount.
            
            var followers = await followService.GetFollowersAsync(userId, new PaginationParams { PageNumber = 1, PageSize = 1 });
            if (followers.TotalCount < 500)
                throw new InvalidOperationException($"You need at least 500 followers to apply. You currently have {followers.TotalCount}.");

            // 4. Create request
            var request = new VerificationRequest
            {
                UserId = userId,
                User = user, // EF might need this or just ID
                SubmittedAt = DateTimeOffset.UtcNow,
                Status = VerificationStatus.Pending
            };

            context.VerificationRequests.Add(request);
            await context.SaveChangesAsync();
        }

        public async Task<PaginatedResult<VerificationRequestDto>> GetPendingRequestsAsync(int page, int limit)
        {
            var query = context.VerificationRequests
                .Include(r => r.User)
                .Where(r => r.Status == VerificationStatus.Pending)
                .OrderBy(r => r.SubmittedAt); // Oldest first

            var total = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(r => new VerificationRequestDto(
                    r.Id,
                    r.UserId,
                    r.User.UserName!,
                    r.User.ProfileImageUrl!, // Assuming nullable handling
                    r.SubmittedAt,
                    r.Status,
                    r.AdminComment
                ))
                .ToListAsync();

            return new PaginatedResult<VerificationRequestDto>(items, total, page, limit);
        }

        public async Task ApproveRequestAsync(int requestId, string adminId)
        {
            var request = await context.VerificationRequests
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == requestId)
                ?? throw new KeyNotFoundException($"Request {requestId} not found.");

            if (request.Status != VerificationStatus.Pending)
                throw new InvalidOperationException("Request is not pending.");

            request.Status = VerificationStatus.Approved;
            request.AdminId = adminId;
            
            // Update user
            var user = await userManager.FindByIdAsync(request.UserId);
            if (user != null)
            {
                user.IsVerified = true;
                await userManager.UpdateAsync(user);
            }
            
            await context.SaveChangesAsync();
        }

        public async Task RejectRequestAsync(int requestId, string adminId, string reason)
        {
             var request = await context.VerificationRequests
                .FirstOrDefaultAsync(r => r.Id == requestId)
                ?? throw new KeyNotFoundException($"Request {requestId} not found.");

            if (request.Status != VerificationStatus.Pending)
                throw new InvalidOperationException("Request is not pending.");

            request.Status = VerificationStatus.Rejected;
            request.AdminId = adminId;
            request.AdminComment = reason;

            await context.SaveChangesAsync();
        }

        public async Task<VerificationStatus?> GetUserVerificationStatusAsync(string userId)
        {
             // Check if user is verified first??
             // Actually request status is more detailed.
             var request = await context.VerificationRequests
                 .Where(r => r.UserId == userId)
                 .OrderByDescending(r => r.SubmittedAt)
                 .FirstOrDefaultAsync();

            return request?.Status;
        }
    }
}
