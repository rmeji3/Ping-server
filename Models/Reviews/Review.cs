using System.ComponentModel.DataAnnotations;
using Conquest.Models.Places;

namespace Conquest.Models.Reviews;

public class Review
{
    public int Id { get; init; }
    [MaxLength(200)]
    public string UserId { get; init; } = null!;  // FK reference only
    [MaxLength(100)]
    public string UserName { get; init; } = null!;
    public int PlaceActivityId { get; set; }
    public PlaceActivity PlaceActivity { get; set; } = null!;
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    public int Rating { get; init; }
    public ReviewType Type { get; set; }
    [MaxLength(1000)]
    public string? Content { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<ReviewTag> ReviewTags { get; set; } = new();
    public int Likes { get; set; }
    public List<ReviewLike> LikesList { get; set; } = new();
}
