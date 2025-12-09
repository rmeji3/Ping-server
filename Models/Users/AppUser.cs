using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Conquest.Models.AppUsers
{
    public class AppUser : IdentityUser
    {
        public required string FirstName { get; set; }
        public required string LastName { get; set; }

        [MaxLength(512)]
        public string? ProfileImageUrl { get; set; } // nullable, NOT required

        public PrivacyConstraint ReviewsPrivacy { get; set; } = PrivacyConstraint.Public;
        public PrivacyConstraint PlacesPrivacy { get; set; } = PrivacyConstraint.Public;
        public PrivacyConstraint LikesPrivacy { get; set; } = PrivacyConstraint.Public;
    }

    public enum PrivacyConstraint
    {
        Public = 0,
        FriendsOnly = 1,
        Private = 2
    }
}
