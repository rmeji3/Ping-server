using System.Text.Json;
using StackExchange.Redis;

namespace Conquest.Services.Redis;

/// <summary>
/// Redis service implementation using StackExchange.Redis.
/// </summary>
public class RedisService : IRedisService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisService> _logger;
    private const string KeyPrefix = "Conquest:";

    public RedisService(IConnectionMultiplexer redis, ILogger<RedisService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        try
        {
            var prefixedKey = GetPrefixedKey(key);
            var serialized = JsonSerializer.Serialize(value);
            return await _db.StringSetAsync(prefixedKey, serialized, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting Redis key: {Key}", key);
            throw;
        }
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var prefixedKey = GetPrefixedKey(key);
            var value = await _db.StringGetAsync(prefixedKey);
            
            if (!value.HasValue)
                return null;

            return JsonSerializer.Deserialize<T>(value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Redis key: {Key}", key);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        try
        {
            var prefixedKey = GetPrefixedKey(key);
            return await _db.KeyDeleteAsync(prefixedKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Redis key: {Key}", key);
            throw;
        }
    }

    public async Task<long> IncrementAsync(string key, TimeSpan? expiry = null)
    {
        try
        {
            var prefixedKey = GetPrefixedKey(key);
            var value = await _db.StringIncrementAsync(prefixedKey);
            
            // Set expiry only if this is a new key (value == 1) or expiry is specified
            if (expiry.HasValue && value == 1)
            {
                await _db.KeyExpireAsync(prefixedKey, expiry.Value);
            }
            
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error incrementing Redis key: {Key}", key);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            var prefixedKey = GetPrefixedKey(key);
            return await _db.KeyExistsAsync(prefixedKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Redis key existence: {Key}", key);
            throw;
        }
    }

    public async Task<TimeSpan?> GetTtlAsync(string key)
    {
        try
        {
            var prefixedKey = GetPrefixedKey(key);
            return await _db.KeyTimeToLiveAsync(prefixedKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting TTL for Redis key: {Key}", key);
            throw;
        }
    }

    private static string GetPrefixedKey(string key) => $"{KeyPrefix}{key}";
}
