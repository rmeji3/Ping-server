namespace Ping.Dtos.Business;

public record BusinessPingAnalyticsDto(
    int PingId,
    int TotalViews,
    int TotalFavorites,
    int TotalReviews,
    double AvgRating,
    int EventCount,
    List<PingDailyStatDto> ViewsHistory,
    List<int> PeakHours // 24 buckets? Or let's say "Hour -> Count" map? Array of 24 ints is easiest.
);

public record PingDailyStatDto(
    DateOnly Date,
    int Value
);

