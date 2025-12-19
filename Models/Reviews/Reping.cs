using System.ComponentModel.DataAnnotations;
using Ping.Models.AppUsers;

namespace Ping.Models.Reviews;

public class Reping
{
    public int Id { get; init; }
    
    [MaxLength(200)]
    public string UserId { get; init; } = null!; // User who repinged
    
    public int ReviewId { get; init; }
    public Review Review { get; init; } = null!;
    
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    public PrivacyConstraint Privacy { get; set; } = PrivacyConstraint.Public;
}
