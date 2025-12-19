using System.ComponentModel.DataAnnotations;
using Ping.Models.Pings;

namespace Ping.Models.Business
{
    public class PingClaim
    {
        public int Id { get; set; }

        [MaxLength(100)]
        public required string UserId { get; set; } // Logical FK to AuthDbContext.AppUser

        public int PingId { get; set; }
        public Ping.Models.Pings.Ping Ping { get; set; } = null!;

        [MaxLength(500)]
        public string Proof { get; set; } = string.Empty; // URL or description

        public ClaimStatus Status { get; set; } = ClaimStatus.Pending;

        public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

        public DateTime? ReviewedUtc { get; set; }
        
        [MaxLength(100)]
        public string? ReviewerId { get; set; } // Admin who reviewed
    }

    public enum ClaimStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2
    }
}

