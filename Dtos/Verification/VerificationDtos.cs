using Ping.Models.Users; // For VerificationStatus enum

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
        string Reason
    );
}
