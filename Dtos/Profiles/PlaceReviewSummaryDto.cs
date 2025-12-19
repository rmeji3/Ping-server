namespace Ping.Dtos.Profiles;

public record PlaceReviewSummaryDto(
    int PingId,
    string PingName,
    string PingAddress,
    double UserRating,
    int ReviewCount,
    List<string> Thumbnails
);
