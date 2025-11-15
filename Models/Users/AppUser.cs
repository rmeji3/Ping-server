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
    }
}
