using System.ComponentModel.DataAnnotations;
using Ping.Models.Reviews;

namespace Ping.Models.Pings;

public class PingActivity
{
    public int Id { get; init; }

    public int PingId { get; init; }
    public Ping Ping { get; init; } = null!;

    [MaxLength(200)]
    public required string Name { get; init; }  // user-facing label

    // nav
    public List<Review> Reviews { get; init; } = [];
    
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}

