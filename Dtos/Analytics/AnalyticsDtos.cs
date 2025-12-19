namespace Ping.Dtos.Analytics;

public record DashboardStatsDto(
    int Dau,
    int Wau,
    int Mau,
    int TotalUsers,
    int NewUsersToday
);

public record TrendingPingDto(
    int PingId,
    string Name,
    int ReviewCount,
    int CheckInCount,
    int TotalInteractions
);

public record ModerationStatsDto(
    int PendingReports,
    int BannedUsers,
    int BannedIps,
    int RejectedReviews // If we tracked this, currently might need to count logic
);

