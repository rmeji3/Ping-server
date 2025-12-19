using System.ComponentModel.DataAnnotations;

namespace Ping.Dtos.Reviews;

public record ReviewDto(
    int Id,
    int Rating,
    string? Content,
    string UserId,
    string UserName,
    string? ProfilePictureUrl,
    string ImageUrl,
    DateTime CreatedAt,
    int Likes,
    bool IsLiked,
    List<string> Tags
);

public record CreateReviewDto(
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    int Rating,
    [MaxLength(1000, ErrorMessage = "Content must be at most 1000 characters.")]
    string? Content,
    [Required]
    string ImageUrl,
    List<string>? Tags = null
);

public record ExploreReviewDto(
    int ReviewId,
    int PingActivityId,
    int PingId,
    string PingName,
    string PingAddress,
    string ActivityName,
    string? PingGenreName,
    double Latitude,
    double Longitude,
    int Rating,
    string? Content,
    string UserId,
    string UserName,
    string? ProfilePictureUrl,
    string ImageUrl,
    DateTime CreatedAt,
    int Likes,
    bool IsLiked,
    List<string> Tags,
    bool IsPingDeleted
);

public class ExploreReviewsFilterDto
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? RadiusKm { get; set; }
    public string? SearchQuery { get; set; }
    public List<int>? PingGenreIds { get; set; }
    public int PageSize { get; set; } = 20;
    public int PageNumber { get; set; } = 1;
}
