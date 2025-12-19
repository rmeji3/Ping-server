using Ping.Dtos.Verification;
using Ping.Dtos.Common;

namespace Ping.Services.Verification
{
    public interface IVerificationService
    {
        Task ApplyAsync(string userId);
        Task<PaginatedResult<VerificationRequestDto>> GetPendingRequestsAsync(int page, int limit);
        Task ApproveRequestAsync(int requestId, string adminId);
        Task RejectRequestAsync(int requestId, string adminId, string reason);
        Task<VerificationStatus?> GetUserVerificationStatusAsync(string userId);
    }
}
