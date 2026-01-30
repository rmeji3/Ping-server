using Ping.Dtos.Search;
using Ping.Dtos.Common;
using Ping.Services.Profiles;
using Ping.Services.Pings;

using Ping.Models.Pings;
using Ping.Data.App;
using Ping.Models.Search;
using Microsoft.EntityFrameworkCore;

namespace Ping.Services.Search;

public class SearchService(
    IProfileService profileService,
    IPingService pingService,
    AppDbContext appDbContext,
    ILogger<SearchService> logger) : ISearchService
{
    public async Task<UnifiedSearchResultDto> UnifiedSearchAsync(UnifiedSearchFilterDto filter, string? userId)
    {
        logger.LogInformation("Unified search for query: {Query} by User: {UserId}", filter.Query, userId ?? "Anonymous");

        var pagination = new PaginationParams { PageNumber = filter.PageNumber, PageSize = filter.PageSize };

        // 1. Search Profiles
        var profiles = userId != null 
            ? await profileService.SearchProfilesAsync(filter.Query, userId, pagination)
            : new PaginatedResult<Ping.Dtos.Profiles.ProfileDto>(new List<Ping.Dtos.Profiles.ProfileDto>(), 0, filter.PageNumber, filter.PageSize);

        // 2. Search Pings
        var pings = await pingService.SearchNearbyAsync(
            filter.Latitude,
            filter.Longitude,
            filter.RadiusKm,
            filter.Query, // Use query for name search
            filter.ActivityNames,
            filter.PingGenreNames,
            filter.Tags,
            null, // visibility
            null, // type
            userId,
            pagination
        );

        return new UnifiedSearchResultDto(
            profiles,
            pings
        );
    }

    public async Task<List<SearchHistoryDto>> GetSearchHistoryAsync(string userId, int count = 20)
    {
        return await appDbContext.SearchHistories
            .Where(sh => sh.UserId == userId)
            .OrderByDescending(sh => sh.CreatedAt)
            .Take(count)
            .Select(sh => new SearchHistoryDto(
                sh.Id,
                sh.Query,
                sh.Type,
                sh.TargetId,
                sh.ImageUrl,
                sh.CreatedAt
            ))
            .ToListAsync();
    }

    public async Task AddToHistoryAsync(string userId, CreateSearchHistoryDto input)
    {
        // Check for existing identical entry (deduplication logic)
        // If exact same query and types exists recently, maybe just update timestamp?
        // For simplicity, let's delete older duplicate if exists to keep list clean
        var existing = await appDbContext.SearchHistories
            .FirstOrDefaultAsync(sh => sh.UserId == userId 
                                       && sh.Query == input.Query 
                                       && sh.Type == input.Type
                                       && sh.TargetId == input.TargetId);

        if (existing != null)
        {
            appDbContext.SearchHistories.Remove(existing);
        }

        var history = new SearchHistory
        {
            UserId = userId,
            Query = input.Query,
            Type = input.Type,
            TargetId = input.TargetId,
            ImageUrl = input.ImageUrl,
            CreatedAt = DateTime.UtcNow
        };

        appDbContext.SearchHistories.Add(history);
        
        // Ensure limit per user (e.g. keep last 50)
        var count = await appDbContext.SearchHistories.CountAsync(sh => sh.UserId == userId);
        if (count > 50)
        {
            var cleanups = appDbContext.SearchHistories
                .Where(sh => sh.UserId == userId)
                .OrderByDescending(sh => sh.CreatedAt)
                .Skip(50);
            
            appDbContext.SearchHistories.RemoveRange(cleanups);
        }

        await appDbContext.SaveChangesAsync();
    }

    public async Task DeleteHistoryItemAsync(string userId, int historyId)
    {
        var item = await appDbContext.SearchHistories
            .FirstOrDefaultAsync(sh => sh.Id == historyId && sh.UserId == userId);
        
        if (item != null)
        {
            appDbContext.SearchHistories.Remove(item);
            await appDbContext.SaveChangesAsync();
        }
    }

    public async Task ClearHistoryAsync(string userId)
    {
        var items = appDbContext.SearchHistories.Where(sh => sh.UserId == userId);
        appDbContext.SearchHistories.RemoveRange(items);
        await appDbContext.SaveChangesAsync();
    }
}
