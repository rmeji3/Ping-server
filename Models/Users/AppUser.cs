using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Ping.Models.AppUsers
{
    public class AppUser : IdentityUser
    {
        public required string FirstName { get; set; }
        public required string LastName { get; set; }

        [MaxLength(512)]
        public string? ProfileImageUrl { get; set; } // nullable, NOT required

        public PrivacyConstraint ReviewsPrivacy { get; set; } = PrivacyConstraint.Public;
        public PrivacyConstraint PingsPrivacy { get; set; } = PrivacyConstraint.Public;
        public PrivacyConstraint LikesPrivacy { get; set; } = PrivacyConstraint.Public;

        // Banning / Moderation
        public bool IsBanned { get; set; }
        public int BanCount { get; set; }
        [MaxLength(45)] // IPv6 max length
        public string? LastIpAddress { get; set; }
        public string? BanReason { get; set; }

        public DateTimeOffset? LastLoginUtc { get; set; }

        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public enum PrivacyConstraint
    {
        Public = 0,
        FriendsOnly = 1,
        Private = 2
    }
}

