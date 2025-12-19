using Ping.Data.App;
using Ping.Data.Auth;
using Ping.Dtos.Analytics;
using Ping.Models.Analytics; // For DailySystemMetric, UserActivityLog
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ping.Models.AppUsers; // For AppUser
using Ping.Models; // For Place, Review, etc.
using Ping.Models.Reviews;
using Ping.Models.Reports;

using Ping.Models.Pings;

namespace Ping.Services.Analytics;

public class AnalyticsService(
    AuthDbContext authDb,
    AppDbContext appDb,
    ILogger<AnalyticsService> logger) : IAnalyticsService
{
    public async Task<DashboardStatsDto> GetDashboardStatsAsync()
    {
        logger.LogInformation("Fetching dashboard stats");
        
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sevenDaysAgo = today.AddDays(-6); 
        var thirtyDaysAgo = today.AddDays(-29);

        // DAU: Users active today
        var dau = await authDb.UserActivityLogs
            .CountAsync(l => l.Date == today);
        logger.LogDebug("DAU count: {Dau}", dau);

        // WAU: Distinct users active in last 7 days including today
        var wau = await authDb.UserActivityLogs
            .Where(l => l.Date >= sevenDaysAgo)
            .Select(l => l.UserId)
            .Distinct()
            .CountAsync();
        logger.LogDebug("WAU count: {Wau}", wau);

        // MAU: Distinct users active in last 30 days
        var mau = await authDb.UserActivityLogs
            .Where(l => l.Date >= thirtyDaysAgo)
            .Select(l => l.UserId)
            .Distinct()
            .CountAsync();
        logger.LogDebug("MAU count: {Mau}", mau);

        // Total Users
        var totalUsers = await authDb.Users.CountAsync();
        logger.LogDebug("Total users: {TotalUsers}", totalUsers);

        // New Users Today (Approximation: Users created today? We don't have CreatedAt. 
        // We'll use: Users whose FIRST activity log is today)
        // This is expensive if table is huge, but for now it's okay.
        // Optimization: Add CreatedAt to AppUser.
        // Fallback: Just return 0 or rely on DailyMetric "NewUsers" logic if we had it.
        // Let's try to query Users joined with Logs? No.
        // Let's just use TotalUsers for now and skip NewUsers logic in real-time to save perf,
        // or approximate by looking at Logs where LoginCount=1 and it is the ONLY log for that user? No.
        
        // Revised: Return 0 for NewUsersToday until we have CreatedAt column or better tracking.
        var newUsers = 0; 

        logger.LogInformation("Dashboard stats fetched: DAU={Dau}, WAU={Wau}, MAU={Mau}, TotalUsers={TotalUsers}", dau, wau, mau, totalUsers);
        return new DashboardStatsDto(dau, wau, mau, totalUsers, newUsers);
    }

    public async Task<List<DailySystemMetric>> GetHistoricalGrowthAsync(string metricType, int days)
    {
        logger.LogInformation("Fetching historical growth for metricType={MetricType}, days={Days}", metricType, days);
        
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
        var metrics = await authDb.DailySystemMetrics
            .Where(m => m.MetricType == metricType && m.Date >= cutoff)
            .OrderBy(m => m.Date)
            .ToListAsync();
        
        logger.LogDebug("Retrieved {Count} historical metrics for {MetricType}", metrics.Count, metricType);
        return metrics;
    }

    public async Task<List<DailySystemMetric>> GetGrowthByRegionAsync()
    {
        logger.LogInformation("Fetching growth by region");
        
        // Example: Returning latest region stats
        // In reality, this depends on "Dimensions" JSON in DailySystemMetric
        // For now, return empty or implement basic logic if metrics exist
        var metrics = await authDb.DailySystemMetrics
            .Where(m => m.MetricType == "RegionGrowth")
            .OrderByDescending(m => m.Date)
            .Take(7)
            .ToListAsync();
        
        logger.LogDebug("Retrieved {Count} region growth metrics", metrics.Count);
        return metrics;
    }

    public async Task<List<TrendingPingDto>> GetTrendingPingsAsync()
    {
        logger.LogInformation("Fetching trending pings");
        
        // Trending: Pings with most Reviews/CheckIns in last 7 days
        var cutoff = DateTime.UtcNow.AddDays(-7);

        var trending = await appDb.Reviews
            .Where(r => r.CreatedAt >= cutoff && r.PingActivity!.Ping.Visibility == PingVisibility.Public)
            .GroupBy(r => r.PingActivity!.Ping)
            .Select(g => new
            {
                Ping = g.Key,
                Count = g.Count(),
                Reviews = g.Count(x => x.Type == ReviewType.Review),
                CheckIns = g.Count(x => x.Type == ReviewType.CheckIn)
            })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        logger.LogDebug("Found {Count} trending pings", trending.Count);
        
        return trending.Select(t => new TrendingPingDto(
            t.Ping.Id,
            t.Ping.Name,
            t.Reviews,
            t.CheckIns,
            t.Count
        )).ToList();
    }

    public async Task<ModerationStatsDto> GetModerationStatsAsync()
    {
        logger.LogInformation("Fetching moderation stats");
        
        var pendingReports = await appDb.Reports
            .CountAsync(r => r.Status == ReportStatus.Pending);

        var bannedUsers = await authDb.Users
            .CountAsync(u => u.IsBanned);

        var bannedIps = await authDb.IpBans
            .CountAsync();

        logger.LogDebug("Moderation stats: PendingReports={PendingReports}, BannedUsers={BannedUsers}, BannedIPs={BannedIPs}", 
            pendingReports, bannedUsers, bannedIps);
        
        return new ModerationStatsDto(pendingReports, bannedUsers, bannedIps, 0);
    }

    public async Task ComputeDailyMetricsAsync(DateOnly date)
    {
        logger.LogInformation("Computing daily metrics for date={Date}", date);
        
        // Idempotency: Check if already computed?
        // We can overwrite or skip. Let's delete existing for this date/type to avoid dupes.
        
        // 1. DAU
        var dauCount = await authDb.UserActivityLogs.CountAsync(l => l.Date == date);
        await SaveMetric(date, "DAU", dauCount);
        logger.LogDebug("Saved DAU metric: {DauCount} for {Date}", dauCount, date);

        // 2. Total Users (Snapshot)
        var totalUsers = await authDb.Users.CountAsync();
        await SaveMetric(date, "TotalUsers", totalUsers);
        logger.LogDebug("Saved TotalUsers metric: {TotalUsers} for {Date}", totalUsers, date);
        
        // 3. New Users (Requires CreatedAt or diffing TotalUsers with yesterday? messy). 
        // Skip for now.

        // 4. Trending Region (if we had IpAddress geolocation) - Placeholder
        
        logger.LogInformation("Completed computing daily metrics for {Date}", date);
    }

    private async Task SaveMetric(DateOnly date, string type, double value, string? dimensions = null)
    {
        var existing = await authDb.DailySystemMetrics
            .FirstOrDefaultAsync(m => m.Date == date && m.MetricType == type);

        if (existing != null)
        {
            logger.LogDebug("Updating existing metric: {Type} for {Date}, oldValue={OldValue}, newValue={NewValue}", 
                type, date, existing.Value, value);
            existing.Value = value;
            existing.Dimensions = dimensions;
        }
        else
        {
            logger.LogDebug("Creating new metric: {Type} for {Date}, value={Value}", type, date, value);
            authDb.DailySystemMetrics.Add(new DailySystemMetric
            {
                Date = date,
                MetricType = type,
                Value = value,
                Dimensions = dimensions
            });
        }
        await authDb.SaveChangesAsync();
    }
}

