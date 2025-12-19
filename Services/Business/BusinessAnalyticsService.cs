using Ping.Data.App;
using Ping.Models.Business;
using Microsoft.EntityFrameworkCore;
using Ping.Dtos.Business;
using Ping.Models.Reviews;

namespace Ping.Services.Business;

public interface IBusinessAnalyticsService
{
    Task TrackPingViewAsync(int pingId);
    Task<BusinessPingAnalyticsDto> GetPingAnalyticsAsync(int pingId);
}

public class BusinessAnalyticsService(AppDbContext db, ILogger<BusinessAnalyticsService> logger) : IBusinessAnalyticsService
{
    public async Task TrackPingViewAsync(int pingId)
    {
        logger.LogDebug("Tracking view for pingId={PingId}", pingId);
        
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        
        var metric = await db.PingDailyMetrics
            .FirstOrDefaultAsync(m => m.PingId == pingId && m.Date == today);
            
        if (metric == null)
        {
            logger.LogDebug("Creating new daily metric for pingId={PingId}, date={Date}", pingId, today);
            metric = new PingDailyMetric
            {
                PingId = pingId,
                Date = today,
                ViewCount = 1
            };
            db.PingDailyMetrics.Add(metric);
        }
        else
        {
            metric.ViewCount++;
            logger.LogDebug("Incremented view count for pingId={PingId}, date={Date}, newCount={ViewCount}", pingId, today, metric.ViewCount);
        }
        
        await db.SaveChangesAsync();
    }

    public async Task<BusinessPingAnalyticsDto> GetPingAnalyticsAsync(int pingId)
    {
        logger.LogInformation("Fetching analytics for pingId={PingId}", pingId);
        
        // 1. Basic Stats
        var ping = await db.Pings
            .Where(p => p.Id == pingId)
            .Select(p => new 
            {
                p.Favorites,
                TotalReviews = p.PingActivities.SelectMany(pa => pa.Reviews).Count(),
                // Avg Rating calculation might be expensive if many reviews, but let's do simple avg for now
                // Actually, accessing Reviews across PlaceActivities needs SelectMany
                AvgRating = p.PingActivities.SelectMany(pa => pa.Reviews)
                    .Where(r => r.Type == ReviewType.Review)
                    .Average(r => (double?)r.Rating) ?? 0.0,
            })
            .FirstOrDefaultAsync();

        if (ping == null)
        {
            logger.LogWarning("Ping not found for analytics: pingId={PingId}", pingId);
            throw new KeyNotFoundException("Ping not found");
        }
        
        logger.LogDebug("Basic stats for pingId={PingId}: Favorites={Favorites}, TotalReviews={TotalReviews}, AvgRating={AvgRating:F2}", 
            pingId, ping.Favorites, ping.TotalReviews, ping.AvgRating);

        // 2. Event Count
        var eventCount = await db.Events.CountAsync(e => e.PingId == pingId);
        logger.LogDebug("Event count for pingId={PingId}: {EventCount}", pingId, eventCount);

        // 3. Views History (Last 30 days)
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var viewsHistory = await db.PingDailyMetrics
            .Where(m => m.PingId == pingId && m.Date >= cutoff)
            .OrderBy(m => m.Date)
            .Select(m => new PingDailyStatDto(m.Date, m.ViewCount))
            .ToListAsync();
            
        var totalViews = await db.PingDailyMetrics
            .Where(m => m.PingId == pingId)
            .SumAsync(m => m.ViewCount);
        logger.LogDebug("Views for pingId={PingId}: TotalViews={TotalViews}, HistoryDays={HistoryDays}", 
            pingId, totalViews, viewsHistory.Count);

        // 4. Peak Hours (from Reviews/CheckIns timestamps)
        // Group by Hour of CreatedAt
        var hours = await db.Reviews
            .Where(r => r.PingActivity!.PingId == pingId) // This navigation might rely on PingActivity
            .GroupBy(r => r.CreatedAt.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToListAsync();
            
        var peakHours = new List<int>(new int[24]); // 0-23 initialized to 0
        foreach (var h in hours)
        {
            if (h.Hour >= 0 && h.Hour < 24) 
                peakHours[h.Hour] = h.Count;
        }
        
        // Find actual peak hour for logging
        var peakHour = peakHours.IndexOf(peakHours.Max());
        logger.LogDebug("Peak hours calculated for pingId={PingId}, peakHour={PeakHour} with {PeakCount} activities", 
            pingId, peakHour, peakHours.Max());

        logger.LogInformation("Analytics fetched for pingId={PingId}: TotalViews={TotalViews}, Favorites={Favorites}, Reviews={Reviews}, Events={Events}", 
            pingId, totalViews, ping.Favorites, ping.TotalReviews, eventCount);
        
        return new BusinessPingAnalyticsDto(
            pingId,
            totalViews,
            ping.Favorites,
            ping.TotalReviews,
            ping.AvgRating,
            eventCount,
            viewsHistory,
            peakHours
        );
    }
}

