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
    private record VibeAnalysis(List<string> SearchTerms, string SuggestedGenre);

    public async Task<List<RecommendationDto>> GetRecommendationsAsync(string vibe, double lat, double lng, double radiusKm)
    {
        // 1. Use Semantic Kernel to analyze the "vibe" and extract search terms
        var analysis = await AnalyzeVibeAsync(vibe, lat, lng);
        var searchTerms = analysis.SearchTerms;
        var suggestedGenre = analysis.SuggestedGenre;

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
            LocalPingId = p.Id,
            Genre = p.PingGenre?.Name ?? suggestedGenre
        }).ToList();
        
        // 3. Fallback to Google Places API if local results are insufficient
        var googleResults = new List<RecommendationDto>();
        if (localResults.Count < 5)
        {
            logger.LogInformation("Insufficient local matches ({Count}). Falling back to Google Places.", localResults.Count);
            
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
                LocalPingId = null,
                Genre = suggestedGenre
            }));
        }

        // Combine results (deduplicate by name roughly) and take top 5
        var combinedResults = localResults
            .Concat(googleResults)
            .GroupBy(r => r.Name)
            .Select(g => g.First())
            .Take(5)
            .ToList();

        if (combinedResults.Count == 0)
        {
            return [];
        }

        // 4. Generate specific reasoning for each candidate in a batch AI call
        await GenerateSpecificReasoningAsync(vibe, combinedResults);

        return combinedResults;
    }

    private async Task GenerateSpecificReasoningAsync(string vibe, List<RecommendationDto> recommendations)
    {
        try
        {
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            
            var placesList = string.Join("\n", recommendations.Select((r, i) => $"{i+1}. {r.Name} ({r.Address})"));

            var prompt = $"""
                          User is looking for a place matching this vibe: "{vibe}"
                          
                          I have found {recommendations.Count} candidate places. For EACH place, write a very short (max 12 words) specific reason why it matches the user's vibe description. 
                          Be creative and mention specific qualities of the place if the name suggests them.
                          
                          Places:
                          {placesList}
                          
                          Output a JSON array of strings, where each string corresponds to the reasoning for place 1, 2, 3, etc.
                          Output only the valid JSON array of strings.
                          """;

            var result = await chatCompletionService.GetChatMessageContentAsync(prompt);
            var text = result.Content;

            if (!string.IsNullOrWhiteSpace(text))
            {
                text = text.Replace("```json", "").Replace("```", "").Trim();
                var reasons = System.Text.Json.JsonSerializer.Deserialize<List<string>>(text);
                
                if (reasons != null && reasons.Count == recommendations.Count)
                {
                    for (int i = 0; i < recommendations.Count; i++)
                    {
                        recommendations[i].Reasoning = reasons[i];
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating specific reasoning for vibe search");
        }

        // Fallback reasoning if AI fails
        foreach (var rec in recommendations)
        {
            rec.Reasoning ??= $"Great match for your {vibe} vibe.";
        }
    }

    private async Task<VibeAnalysis> AnalyzeVibeAsync(string vibe, double lat, double lng)
    {
        try
        {
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            var prompt = $"""
                          You are a helpful assistant that translates a "vibe" or "feeling" into concrete search queries for places.
                          User Vibe: "{vibe}"
                          User Location: Latitude {lat}, Longitude {lng}
                          
                          Output a JSON object with two fields:
                          1. "SearchTerms": A list of 1-3 specific search terms (e.g., ["coffee shop", "library", "park"]) that would match this vibe.
                          2. "SuggestedGenre": A single category name that best describes these places (e.g., "Food", "Study", "Nightlife", "Outdoors"). Use "General" if unsure.
                          
                          IMPORTANT: If the location suggests a non-English speaking country, include terms in the LOCAL language as well as English.
                          Output only the valid JSON.
                          """;

            var result = await chatCompletionService.GetChatMessageContentAsync(prompt);
            var text = result.Content;

            if (string.IsNullOrWhiteSpace(text)) return new VibeAnalysis([], "General");

            // Basic cleanup of markdown code blocks if AI included them
            text = text.Replace("```json", "").Replace("```", "").Trim();

            var analysis = System.Text.Json.JsonSerializer.Deserialize<VibeAnalysis>(text);
            return analysis ?? new VibeAnalysis([], "General");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error analyzing vibe with Semantic Kernel");
            // Fallback: just use the vibe itself as a keyword if AI fails
            return new VibeAnalysis([vibe], "General");
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

