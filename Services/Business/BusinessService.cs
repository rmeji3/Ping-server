using Ping.Data.App;
using Ping.Dtos.Business;
using Ping.Models.Business;
using Ping.Models.Pings;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Ping.Models.AppUsers;

namespace Ping.Services
{
    public class BusinessService : IBusinessService
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;

        public BusinessService(AppDbContext context, UserManager<AppUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<PingClaim> SubmitClaimAsync(string userId, CreateClaimDto dto)
        {
            var ping = await _context.Pings.FindAsync(dto.PingId);
            if (ping == null)
            {
                throw new ArgumentException("Ping not found");
            }

            // Check if already claimed by this user or pending
            var existing = await _context.PingClaims
                .AnyAsync(c => c.UserId == userId && c.PingId == dto.PingId && c.Status == ClaimStatus.Pending);
            
            if (existing)
            {
                throw new InvalidOperationException("You already have a pending claim for this ping.");
            }

            var claim = new PingClaim
            {
                UserId = userId,
                PingId = dto.PingId,
                Proof = dto.Proof
            };

            _context.PingClaims.Add(claim);
            await _context.SaveChangesAsync();
            return claim;
        }

        public async Task<List<ClaimDto>> GetPendingClaimsAsync()
        {
            var claims = await _context.PingClaims
                .Include(c => c.Ping)
                .Where(c => c.Status == ClaimStatus.Pending)
                .OrderByDescending(c => c.CreatedUtc)
                .ToListAsync();

            var userIds = claims.Select(c => c.UserId).Distinct().ToList();
            var users = await _userManager.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName);

            return claims.Select(c => new ClaimDto(
                c.Id,
                c.PingId,
                c.Ping.Name,
                c.UserId,
                users.ContainsKey(c.UserId) ? users[c.UserId] ?? "Unknown" : "Unknown",
                c.Proof,
                c.Status,
                c.CreatedUtc,
                c.ReviewedUtc
            )).ToList();
        }

        public async Task ApproveClaimAsync(int claimId, string reviewerId)
        {
            var claim = await _context.PingClaims
                .Include(c => c.Ping)
                .FirstOrDefaultAsync(c => c.Id == claimId);

            if (claim == null) throw new ArgumentException("Claim not found");
            if (claim.Status != ClaimStatus.Pending) throw new InvalidOperationException("Claim is not pending");

            // 1. Transfer Ownership
            claim.Ping.OwnerUserId = claim.UserId;
            claim.Ping.IsClaimed = true; // Mark as claimed
            // Optionally set Type? Maybe not needed if logic handles it.
            // Verified places are usually the target.
            
            // 2. Update Claim
            claim.Status = ClaimStatus.Approved;
            claim.ReviewedUtc = DateTime.UtcNow;
            claim.ReviewerId = reviewerId;

            // 3. Add Role (Business)
            var user = await _userManager.FindByIdAsync(claim.UserId);
            if (user != null)
            {
                if (!await _userManager.IsInRoleAsync(user, "Business"))
                {
                    await _userManager.AddToRoleAsync(user, "Business");
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task RejectClaimAsync(int claimId, string reviewerId)
        {
            var claim = await _context.PingClaims.FindAsync(claimId);
            if (claim == null) throw new ArgumentException("Claim not found");
            if (claim.Status != ClaimStatus.Pending) throw new InvalidOperationException("Claim is not pending");

            claim.Status = ClaimStatus.Rejected;
            claim.ReviewedUtc = DateTime.UtcNow;
            claim.ReviewerId = reviewerId;

            await _context.SaveChangesAsync();
        }
    }
}

