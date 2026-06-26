namespace Ping.Services.AI;

public interface IPingGenreClassifier
{
    /// <summary>
    /// Attempts to classify and persist a PingGenreId for the given ping.
    /// No-ops if the ping already has a genre or cannot be found.
    /// </summary>
    Task ClassifyAsync(Ping.Services.Background.PingGenreJob job, CancellationToken ct = default);
}
