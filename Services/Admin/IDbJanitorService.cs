namespace Ping.Services.Admin;

public interface IDbJanitorService
{
    Task<JanitorResult> CleanupFileUrlsAsync();
}

public record JanitorResult(int Reviews, int Events, int Users, int Collections, int History, int Reports);
