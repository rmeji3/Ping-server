using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.AI;

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
}
