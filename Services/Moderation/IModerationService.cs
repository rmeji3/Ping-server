namespace Conquest.Services.Moderation;

public record ModerationResult(bool IsFlagged, string? Reason);

public interface IModerationService
{
    Task<ModerationResult> CheckContentAsync(string text);
}
