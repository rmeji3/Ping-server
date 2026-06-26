using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.Extensions.Logging;

namespace Ping.Services.AI;

public class OpenAISemanticService(IChatCompletionService chat, ILogger<OpenAISemanticService> logger) : ISemanticService
{
    public async Task<string?> FindDuplicateAsync(string newName, IEnumerable<string> existingNames)
    {
        var existingList = existingNames.ToList();
        if (existingList.Count == 0) return null;

        var history = new ChatHistory();
        history.AddSystemMessage(@"You are a semantic analysis assistant. 
Your goal is to prevent duplicate activities at a place.
Compare the 'Candidate Name' against the 'Existing List'.
If the candidate is effectively the same activity as one in the list (e.g. synonyms like 'Hoops' vs 'Basketball', 'Gym' vs 'Fitness Center', or typos), return the EXACT name from the list.
If it is a new/distinct activity, return 'NO'.
Be slightly strict: 'Tennis' and 'Table Tennis' are DIFFERENT. 'Basketball' and 'Outdoor Basketball' could be different, but often same. Prefer merging if very close.");
        
        history.AddUserMessage($@"Existing List: {string.Join(", ", existingList)}
Candidate Name: {newName}");

        try 
        {
            var result = await chat.GetChatMessageContentAsync(history);
            var response = result.Content?.Trim();
            
            if (string.IsNullOrEmpty(response) || response.Equals("NO", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // The AI should return the exact existing name. 
            // We verify it exists to be safe.
            var match = existingList.FirstOrDefault(e => e.Equals(response, StringComparison.OrdinalIgnoreCase));
            
            if (match != null)
            {
                logger.LogInformation("Semantic Deduplication: '{New}' merged into '{Existing}'", newName, match);
                return match;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Semantic service failed");
            return null; // Fail safe (allow creation)
        }
    }

    public async Task<bool> VerifyPlaceNameMatchAsync(string officialName, string userProvidedName)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(@"You are a semantic validation assistant for a map application.
Your task is to determine if 'User Provided Name' is a valid name for the place officially known as 'Official Name'.

Return 'TRUE' if:
- It is a common nickname (e.g. 'The Met' for 'Metropolitan Museum of Art').
- It is an abbreviation (e.g. 'MoMA', 'JFK Airport').
- It is a slight typo or spelling variation.
- It is a more specific descriptor that is accurate (e.g. 'Starbucks on 5th' for 'Starbucks').

Return 'FALSE' if:
- It is completely unrelated (e.g. 'My House' for 'McDonalds').
- It is misleading or offensive.
- It is a different place entirely.

Respond ONLY with 'TRUE' or 'FALSE'.");

        history.AddUserMessage($@"Official Name: {officialName}
User Provided Name: {userProvidedName}");

        try
        {
            var result = await chat.GetChatMessageContentAsync(history);
            var response = result.Content?.Trim();
            
            return response != null && response.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Semantic verification failed");
            // Fail safe: If AI is down, we can default to string equality or return false.
            // Returning false leans on the side of caution (demoting to Custom ping).
            return officialName.Equals(userProvidedName, StringComparison.OrdinalIgnoreCase);
        }
    }
    public async Task<string?> ClassifyGenreAsync(string placeName, string? activityName, IEnumerable<string> validGenres)
    {
        var genreList = validGenres.ToList();
        if (genreList.Count == 0) return null;

        var genreOptions = string.Join(", ", genreList);

        var history = new ChatHistory();
        history.AddSystemMessage($@"You are a genre classifier for a social map app.
Given a place name and optionally an activity done there, return ONLY the single best-matching genre name from the list below.
Prioritize the activity over the place name when choosing the genre (e.g. if the place is a gym/fitness center but the activity is 'Basketball', select 'Sports' rather than 'Wellness').
Do not explain. Do not add punctuation. Return the exact genre name as written in the list.

Valid genres: {genreOptions}

If nothing fits well, return: Other");

        history.AddUserMessage($"Place: \"{placeName}\"\nActivity: \"{activityName ?? "(none)"}\"");

        try
        {
            var result = await chat.GetChatMessageContentAsync(history);
            var response = result.Content?.Trim().Trim('"', '\'', '.') ?? string.Empty;

            // Validate the response is one of the valid genres (case-insensitive).
            var match = genreList.FirstOrDefault(g => g.Equals(response, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                logger.LogInformation("Genre classification: \"{Place}\", Activity: \"{Activity}\" → \"{Genre}\"", placeName, activityName, match);
                return match;
            }

            logger.LogWarning("Genre classification returned unrecognised value \"{Response}\" for place \"{Place}\", Activity \"{Activity}\"", response, placeName, activityName);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Genre classification failed for place \"{Place}\", Activity \"{Activity}\"", placeName, activityName);
            return null;
        }
    }
}

