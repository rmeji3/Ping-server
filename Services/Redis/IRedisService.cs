namespace Conquest.Services.Redis;

/// <summary>
/// Redis service for distributed caching and rate limiting operations.
/// </summary>
public interface IRedisService
{
    /// <summary>
    /// Set a value in Redis with optional expiration.
    /// </summary>
    Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null);

    /// <summary>
    /// Get a value from Redis.
    /// </summary>
    Task<T?> GetAsync<T>(string key) where T : class;

    /// <summary>
    /// Delete a key from Redis.
    /// </summary>
    Task<bool> DeleteAsync(string key);

    /// <summary>
    /// Increment a counter in Redis, creating it if it doesn't exist.
    /// Returns the new value after increment.
    /// </summary>
    Task<long> IncrementAsync(string key, TimeSpan? expiry = null);

    /// <summary>
    /// Check if a key exists in Redis.
    /// </summary>
    Task<bool> ExistsAsync(string key);

    /// <summary>
    /// Get the time-to-live (TTL) for a key in seconds.
    /// Returns null if key doesn't exist or has no expiry.
    /// </summary>
    Task<TimeSpan?> GetTtlAsync(string key);
}
