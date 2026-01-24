using Ping.Models.Users; // For VerificationStatus enum
using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Verification
{
    public record VerificationRequestDto(
        int Id,
        string UserId,
        string UserName,
        string UserImage,
        DateTimeOffset SubmittedAt,
        VerificationStatus Status,
        string? AdminComment
    );

    public record ApplyVerificationDto(
        // Empty for now, maybe add reason later?
    );

    public record RejectVerificationDto(
        [Required, MaxLength(500)] string Reason
    );
}
