using System.ComponentModel.DataAnnotations;
using Conquest.Models.Reviews;
using Conquest.Models.Activities;

namespace Conquest.Models.Places;

public class PlaceActivity
{
    public int Id { get; init; }

    public int PlaceId { get; init; }
    public Place Place { get; init; } = null!;

    public int? ActivityKindId { get; init; }
    public ActivityKind? ActivityKind { get; init; }

    [MaxLength(200)]
    public required string Name { get; init; }  // user-facing label

    // nav
    public List<Review> Reviews { get; init; } = [];
    
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}
