using Conquest.Data.App;
using Conquest.Dtos.Business;
using Conquest.Models.Business;
using Conquest.Models.Places;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Conquest.Models.AppUsers;

namespace Conquest.Services
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

        public async Task<PlaceClaim> SubmitClaimAsync(string userId, CreateClaimDto dto)
        {
            var place = await _context.Places.FindAsync(dto.PlaceId);
            if (place == null)
            {
                throw new ArgumentException("Place not found");
            }

            // Check if already claimed by this user or pending
            var existing = await _context.PlaceClaims
                .AnyAsync(c => c.UserId == userId && c.PlaceId == dto.PlaceId && c.Status == ClaimStatus.Pending);
            
            if (existing)
            {
                throw new InvalidOperationException("You already have a pending claim for this place.");
            }

            var claim = new PlaceClaim
            {
                UserId = userId,
                PlaceId = dto.PlaceId,
                Proof = dto.Proof
            };

            _context.PlaceClaims.Add(claim);
            await _context.SaveChangesAsync();
            return claim;
        }

        public async Task<List<ClaimDto>> GetPendingClaimsAsync()
        {
            var claims = await _context.PlaceClaims
                .Include(c => c.Place)
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
                c.PlaceId,
                c.Place.Name,
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
            var claim = await _context.PlaceClaims
                .Include(c => c.Place)
                .FirstOrDefaultAsync(c => c.Id == claimId);

            if (claim == null) throw new ArgumentException("Claim not found");
            if (claim.Status != ClaimStatus.Pending) throw new InvalidOperationException("Claim is not pending");

            // 1. Transfer Ownership
            claim.Place.OwnerUserId = claim.UserId;
            claim.Place.IsClaimed = true; // Mark as claimed
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
            var claim = await _context.PlaceClaims.FindAsync(claimId);
            if (claim == null) throw new ArgumentException("Claim not found");
            if (claim.Status != ClaimStatus.Pending) throw new InvalidOperationException("Claim is not pending");

            claim.Status = ClaimStatus.Rejected;
            claim.ReviewedUtc = DateTime.UtcNow;
            claim.ReviewerId = reviewerId;

            await _context.SaveChangesAsync();
        }
    }
}
