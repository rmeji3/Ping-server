using Microsoft.EntityFrameworkCore;
using Ping.Data.App;
using Ping.Data.Auth;
using Ping.Utils;
using Microsoft.Extensions.Logging;

namespace Ping.Services.Admin;

public class DbJanitorService(
    AppDbContext appDb,
    AuthDbContext authDb,
    ILogger<DbJanitorService> logger) : IDbJanitorService
{
    public async Task<JanitorResult> CleanupFileUrlsAsync()
    {
        logger.LogInformation("Starting Database URL Sanitization...");

        // 1. Cleanup Reviews
        var corruptedReviews = await appDb.Reviews
            .Where(r => r.ImageUrl.StartsWith("file://") || r.ThumbnailUrl.StartsWith("file://"))
            .ToListAsync();

        foreach (var r in corruptedReviews)
        {
            r.ImageUrl = UrlUtils.SanitizeUrl(r.ImageUrl);
            r.ThumbnailUrl = UrlUtils.SanitizeUrl(r.ThumbnailUrl);
        }
        int reviewsCleaned = corruptedReviews.Count;

        // 2. Cleanup Events
        var corruptedEvents = await appDb.Events
            .Where(e => (e.ImageUrl != null && e.ImageUrl.StartsWith("file://")) || 
                        (e.ThumbnailUrl != null && e.ThumbnailUrl.StartsWith("file://")))
            .ToListAsync();

        foreach (var e in corruptedEvents)
        {
            e.ImageUrl = UrlUtils.SanitizeUrl(e.ImageUrl);
            e.ThumbnailUrl = UrlUtils.SanitizeUrl(e.ThumbnailUrl);
        }
        int eventsCleaned = corruptedEvents.Count;

        // 3. Cleanup Users (Profile Images)
        var corruptedUsers = await authDb.Users
            .Where(u => (u.ProfileImageUrl != null && u.ProfileImageUrl.StartsWith("file://")) || 
                        (u.ProfileThumbnailUrl != null && u.ProfileThumbnailUrl.StartsWith("file://")))
            .ToListAsync();

        foreach (var u in corruptedUsers)
        {
            u.ProfileImageUrl = UrlUtils.SanitizeProfileUrl(u.ProfileImageUrl);
            u.ProfileThumbnailUrl = UrlUtils.SanitizeProfileUrl(u.ProfileThumbnailUrl);
        }
        int usersCleaned = corruptedUsers.Count;

        // 4. Cleanup Collections
        var corruptedCollections = await appDb.Collections
            .Where(c => (c.ImageUrl != null && c.ImageUrl.StartsWith("file://")) || 
                        (c.ThumbnailUrl != null && c.ThumbnailUrl.StartsWith("file://")))
            .ToListAsync();

        foreach (var c in corruptedCollections)
        {
            c.ImageUrl = UrlUtils.SanitizeUrl(c.ImageUrl);
            c.ThumbnailUrl = UrlUtils.SanitizeUrl(c.ThumbnailUrl);
        }
        int collectionsCleaned = corruptedCollections.Count;

        // 5. Cleanup Search History
        var corruptedHistory = await appDb.SearchHistories
            .Where(sh => sh.ImageUrl != null && sh.ImageUrl.StartsWith("file://"))
            .ToListAsync();

        foreach (var sh in corruptedHistory)
        {
            sh.ImageUrl = UrlUtils.SanitizeUrl(sh.ImageUrl);
        }
        int historyCleaned = corruptedHistory.Count;

        // 6. Cleanup Reports
        var corruptedReports = await appDb.Reports
            .Where(r => r.ScreenshotUrl != null && r.ScreenshotUrl.StartsWith("file://"))
            .ToListAsync();

        foreach (var r in corruptedReports)
        {
            r.ScreenshotUrl = UrlUtils.SanitizeUrl(r.ScreenshotUrl);
        }
        int reportsCleaned = corruptedReports.Count;

        if (reviewsCleaned > 0 || eventsCleaned > 0 || collectionsCleaned > 0 || historyCleaned > 0 || reportsCleaned > 0) 
            await appDb.SaveChangesAsync();
            
        if (usersCleaned > 0) await authDb.SaveChangesAsync();

        logger.LogInformation("Database Sanitization Complete. Reviews: {Reviews}, Events: {Events}, Users: {Users}, Collections: {Collections}, History: {History}, Reports: {Reports}", 
            reviewsCleaned, eventsCleaned, usersCleaned, collectionsCleaned, historyCleaned, reportsCleaned);

        return new JanitorResult(reviewsCleaned, eventsCleaned, usersCleaned, collectionsCleaned, historyCleaned, reportsCleaned);
    }
}
