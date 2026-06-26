namespace Ping.Services.AI;

public interface ISemanticService
{
    /// <summary>
    /// Checks if the newName is semantically equivalent to any of the existingNames.
    /// Returns the matching existing name, or null if unique.
    /// </summary>
    Task<string?> FindDuplicateAsync(string newName, IEnumerable<string> existingNames);
    
    /// <summary>
    /// Checks if the userProvidedName is a valid variation of the officialName.
    /// Returns true if it's a match (abbreviation, typo, nickname).
    /// Returns false if it's unrelated.
    /// </summary>
    Task<bool> VerifyPlaceNameMatchAsync(string officialName, string userProvidedName);

    /// <summary>
    /// Classifies a place and activity into exactly one genre from the provided list.
    /// Returns the matched genre name, or null if classification fails.
    /// </summary>
    Task<string?> ClassifyGenreAsync(string placeName, string? activityName, IEnumerable<string> validGenres);
}

