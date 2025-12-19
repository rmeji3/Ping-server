using Ping.Dtos.Business;
using Ping.Models.Business;

namespace Ping.Services
{
    public interface IBusinessService
    {
        Task<PingClaim> SubmitClaimAsync(string userId, CreateClaimDto dto);
        Task<List<ClaimDto>> GetPendingClaimsAsync();
        Task ApproveClaimAsync(int claimId, string reviewerId);
        Task RejectClaimAsync(int claimId, string reviewerId);
        // Analytics could go here later or in a separate AnalyticsService
    }
}

