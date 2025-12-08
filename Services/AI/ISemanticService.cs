namespace Conquest.Services.AI;

public interface ISemanticService
{
    /// <summary>
    /// Checks if the newName is semantically equivalent to any of the existingNames.
    /// Returns the matching existing name, or null if unique.
    /// </summary>
    Task<string?> FindDuplicateAsync(string newName, IEnumerable<string> existingNames);
}
