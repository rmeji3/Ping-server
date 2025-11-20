namespace Conquest.Models.Reviews;

public class ReviewLike
{
    public int Id { get; set; }
    public int ReviewId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    
    // Navigation property
    public Review Review { get; set; } = null!;
}