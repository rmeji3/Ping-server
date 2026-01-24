using Ping.Models.Business;
using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Business
{
    public record CreateClaimDto(int PingId, [Required, MaxLength(2048)] string Proof);

    public record ClaimDto(
        int Id,
        int PingId,
        string PingName,
        string UserId,
        string UserName, // Populated if we fetch User info
        string Proof,
        ClaimStatus Status,
        DateTime CreatedUtc,
        DateTime? ReviewedUtc
    );

    // For updates if needed, or simple approve/reject actions
    public record UpdateClaimStatusDto(ClaimStatus Status);
}

