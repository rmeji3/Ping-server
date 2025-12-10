using Conquest.Dtos.Business;
using Conquest.Models.Business;

namespace Conquest.Services
{
    public interface IBusinessService
    {
        Task<PlaceClaim> SubmitClaimAsync(string userId, CreateClaimDto dto);
        Task<List<ClaimDto>> GetPendingClaimsAsync();
        Task ApproveClaimAsync(int claimId, string reviewerId);
        Task RejectClaimAsync(int claimId, string reviewerId);
        // Analytics could go here later or in a separate AnalyticsService
    }
}
