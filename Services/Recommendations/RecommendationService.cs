using Ping.Data.App;
using Ping.Models.Pings;
using Ping.Services.Google;
using Ping.Dtos.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Ping.Services.Recommendations;

public class RecommendationService(
    Kernel kernel,
    AppDbContext dbContext,
    IPingNameService pingNameService,
    ILogger<RecommendationService> logger)
{
    public async Task<List<RecommendationDto>> GetRecommendationsAsync(string vibe, double lat, double lng, double radiusKm)
    {
        // 1. Use Semantic Kernel to analyze the "vibe" and extract search terms
        var searchTerms = await AnalyzeVibeAsync(vibe, lat, lng);
        logger.LogInformation("Vibe '{Vibe}' analyzed into search terms: {Terms}", vibe, string.Join(", ", searchTerms));

        if (searchTerms.Count == 0)
        {
            return [];
        }

        // 2. Search Local Database first
        var localPlaces = await SearchLocalDatabaseAsync(searchTerms, lat, lng, radiusKm);
        var localResults = localPlaces.Select(p => new RecommendationDto
        {
            Name = p.Name,
            Address = p.Address,
            Latitude = p.Latitude,
            Longitude = p.Longitude,
            Source = "Local",
            LocalPingId = p.Id
        }).ToList();
        
        // If we have enough local results, return them (prioritizing local data)
        if (localResults.Count >= 3)
        {
            logger.LogInformation("Found enough local matches ({Count}). Returning local results.", localResults.Count);
            return localResults;
        }

        // 3. Fallback to Google Places API if local results are insufficient
        logger.LogInformation("Insufficient local matches. Falling back to Google Places.");
        var googleResults = new List<RecommendationDto>();
        
        // Use the first (most relevant) search term for the Google query
        var primaryQuery = searchTerms[0]; 
        var googlePings = await pingNameService.SearchPingsAsync(primaryQuery, lat, lng, radiusKm);
        
        googleResults.AddRange(googlePings.Select(g => new RecommendationDto
        {
            Name = g.Name,
            Address = g.Address,
            Latitude = g.Lat,
            Longitude = g.Lng,
            Source = "Google",
            LocalPingId = null
        }));

        // Combine results (deduplicate by name roughly)
        var finalResults = localResults
            .Concat(googleResults)
            .GroupBy(r => r.Name) // Simple dedupe by name
            .Select(g => g.First())
            .ToList();
        
        if (finalResults.Count == 0)
        {
            return [new RecommendationDto 
            { 
                Name = "No places found matching your vibe within this radius.", 
                Address = "Try increasing the radius or changing your vibe.",
                Source = "System",
                Latitude = null,
                Longitude = null,
                LocalPingId = null
            }];
        }

        return finalResults;
    }

    private async Task<List<string>> AnalyzeVibeAsync(string vibe, double lat, double lng)
    {
        try
        {
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            var prompt = $"""
                          You are a helpful assistant that translates a "vibe" or "feeling" into concrete search queries for places.
                          User Vibe: "{vibe}"
                          User Location: Latitude {lat}, Longitude {lng}
                          
                          Output a comma-separated list of 1-3 specific search terms (e.g., "coffee shop", "library", "park") that would match this vibe.
                          IMPORTANT: If the location suggests a non-English speaking country, include terms in the LOCAL language as well as English.
                          Do not include any other text, just the comma-separated terms.
                          """;

            var result = await chatCompletionService.GetChatMessageContentAsync(prompt);
            var text = result.Content;

            if (string.IsNullOrWhiteSpace(text)) return [];

            return text.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error analyzing vibe with Semantic Kernel");
            // Fallback: just use the vibe itself as a keyword if AI fails
            return [vibe];
        }
    }

    private async Task<List<Ping.Models.Pings.Ping>> SearchLocalDatabaseAsync(List<string> searchTerms, double lat, double lng, double radiusKm)
    {
        // Create a point for the center (PostGIS uses Longitude, Latitude order, verify SRID matches)
        var center = new NetTopologySuite.Geometries.Point(lng, lat) { SRID = 4326 };
        
        // IsWithinDistance uses the units of the projection. For 4326, it is degrees.
        // Approx conversion: 1 degree ~ 111 km.
        var distanceDegrees = radiusKm / 111.0;

        var query = dbContext.Pings
            .Include(p => p.PingGenre)
            .Include(p => p.PingActivities)
                .ThenInclude(pa => pa.Reviews)
                    .ThenInclude(r => r.ReviewTags)
                        .ThenInclude(rt => rt.Tag)
            .Where(p => p.Location.IsWithinDistance(center, distanceDegrees));

        var nearbyPings = await query.ToListAsync();

        var matches = nearbyPings
            .Where(p => searchTerms.Any(term => 
                // Match Ping Name
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                // Match Activity Name (e.g. "Pickup Soccer")
                p.PingActivities.Any(pa => pa.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                // Match Ping Genre (e.g. "Sports")
                p.PingGenre?.Name.Contains(term, StringComparison.OrdinalIgnoreCase) == true ||
                // Match Tags on Reviews (e.g. "Cozy", "Crowded")
                p.PingActivities.Any(pa => pa.Reviews.Any(r => r.ReviewTags.Any(rt => rt.Tag.Name.Contains(term, StringComparison.OrdinalIgnoreCase))))
            ))
            .Take(5)
            .ToList();

        return matches;
    }
}

