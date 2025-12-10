namespace Conquest.Dtos.Business;

public record BusinessPlaceAnalyticsDto(
    int PlaceId,
    int TotalViews,
    int TotalFavorites,
    int TotalReviews,
    double AvgRating,
    int EventCount,
    List<PlaceDailyStatDto> ViewsHistory,
    List<int> PeakHours // 24 buckets? Or let's say "Hour -> Count" map? Array of 24 ints is easiest.
);

public record PlaceDailyStatDto(
    DateOnly Date,
    int Value
);
