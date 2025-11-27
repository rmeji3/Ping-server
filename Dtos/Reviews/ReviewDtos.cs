namespace Conquest.Dtos.Reviews;

public record ReviewDto(
    int Id,
    int Rating,
    string? Content,
    string UserId,
    string UserName,
    DateTime CreatedAt,
    int Likes,
    bool IsLiked,
    List<string> Tags
);

public record CreateReviewDto(int Rating, string? Content, List<string>? Tags = null);

public record ExploreReviewDto(
    int ReviewId,
    int PlaceActivityId,
    int PlaceId,
    string PlaceName,
    string PlaceAddress,
    string ActivityName,
    string? ActivityKindName,
    double Latitude,
    double Longitude,
    int Rating,
    string? Content,
    string UserName,
    DateTime CreatedAt,
    int Likes,
    bool IsLiked,
    List<string> Tags
);

public class ExploreReviewsFilterDto
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? RadiusKm { get; set; }
    public string? SearchQuery { get; set; }
    public List<int>? ActivityKindIds { get; set; }
    public int PageSize { get; set; } = 20;
    public int PageNumber { get; set; } = 1;
}