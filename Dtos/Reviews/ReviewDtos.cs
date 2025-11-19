namespace Conquest.Dtos.Reviews;

public record ReviewDto(int Id, int Rating, string? Content, string UserName, DateTime CreatedAt);
public record CreateReviewDto(int Rating, string? Content);
public record ExploreReviewDto(
    int ReviewId,
    int PlaceActivityId,
    int PlaceId,
    string PlaceName,
    string PlaceAddress,
    double Latitude,
    double Longitude,
    int Rating,
    string? Content,
    string UserName,
    DateTime CreatedAt
);