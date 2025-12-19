using Ping.Models.AppUsers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ping.Models.Users
{
    public class VerificationRequest
    {
        [Key]
        public int Id { get; set; }

        public required string UserId { get; set; }
        [ForeignKey("UserId")]
        public AppUser User { get; set; } = null!;

        public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

        public VerificationStatus Status { get; set; } = VerificationStatus.Pending;

        public string? AdminComment { get; set; }

        public string? AdminId { get; set; }
        [ForeignKey("AdminId")]
        public AppUser? Admin { get; set; }
    }

    public enum VerificationStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2
    }
}
