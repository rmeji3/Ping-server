namespace Ping.Services.Moderation;

public record ModerationResult(bool IsFlagged, string? Reason);

public interface IModerationService
{
    Task<ModerationResult> CheckContentAsync(string text);
    Task<ModerationResult> CheckImageAsync(string base64Image);
}

