namespace Ping.Services.Background;

/// <summary>
/// Job enqueued after a ping is created with no genre set.
/// Carries everything the classifier needs without an extra DB round-trip.
/// </summary>
public record PingGenreJob(
    int PingId,
    string? GooglePlaceId,
    string PingName
);
