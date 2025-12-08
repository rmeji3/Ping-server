using System.Security.Claims;
using Conquest.Services.Redis;
using Microsoft.Extensions.Primitives;

namespace Conquest.Middleware;

/// <summary>
/// Rate limiting middleware using Redis for distributed rate limiting.
/// Implements sliding window algorithm with separate limits for anonymous and authenticated users.
/// </summary>
public class RateLimitMiddleware : IMiddleware
{
    private readonly IRedisService _redis;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly IConfiguration _configuration;

    public RateLimitMiddleware(
        IRedisService redis,
        ILogger<RateLimitMiddleware> logger,
        IConfiguration configuration)
    {
        _redis = redis;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Get rate limit configuration
        var globalLimit = _configuration.GetValue<int>("RateLimiting:GlobalLimitPerMinute", 100);
        var authenticatedLimit = _configuration.GetValue<int>("RateLimiting:AuthenticatedLimitPerMinute", 200);
        var authEndpointsLimit = _configuration.GetValue<int>("RateLimiting:AuthEndpointsLimitPerMinute", 5);

        // Determine client identifier and limit
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAuthenticated = !string.IsNullOrEmpty(userId);
        var isAuthEndpoint = context.Request.Path.StartsWithSegments("/api/auth");

        string clientId;
        int limit;

        if (isAuthEndpoint)
        {
            // Auth endpoints: strict IP-based limiting
            clientId = GetClientIp(context);
            limit = authEndpointsLimit;
        }
        else if (isAuthenticated)
        {
            // Authenticated: user-based limiting
            clientId = userId!;
            limit = authenticatedLimit;
        }
        else
        {
            // Anonymous: IP-based limiting
            clientId = GetClientIp(context);
            limit = globalLimit;
        }

        // Create Redis key with current minute window
        var currentMinute = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm");
        var redisKey = $"ratelimit:{clientId}:{currentMinute}";

        try
        {
            // Increment counter and set 1-minute expiry
            var requestCount = await _redis.IncrementAsync(redisKey, TimeSpan.FromMinutes(1));

            // Add rate limit headers
            context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, limit - requestCount).ToString();
            context.Response.Headers["X-RateLimit-Reset"] = GetNextMinuteTimestamp().ToString();

            if (requestCount > limit)
            {
                _logger.LogWarning(
                    "Rate limit exceeded for client {ClientId} on path {Path}. Count: {Count}, Limit: {Limit}",
                    clientId, context.Request.Path, requestCount, limit);

                context.Response.StatusCode = 429; // Too Many Requests
                context.Response.Headers["Retry-After"] = "60";
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Rate limit exceeded",
                    message = $"Too many requests. Please try again in 60 seconds.",
                    retryAfter = 60
                });

                return;
            }
        }
        catch (Exception ex)
        {
            // Redis failure: log and allow request
            _logger.LogError(ex, "Error in rate limiting middleware. Allowing request to proceed.");
        }

        await next(context);
    }

    private static string GetClientIp(HttpContext context)
    {
        // Try to get real IP from X-Forwarded-For header (for reverse proxy scenarios)
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out StringValues forwardedFor))
        {
            var ip = forwardedFor.FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(ip))
                return ip;
        }

        // Fallback to direct connection IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static long GetNextMinuteTimestamp()
    {
        var nextMinute = DateTime.UtcNow.AddMinutes(1);
        var startOfNextMinute = new DateTime(
            nextMinute.Year, nextMinute.Month, nextMinute.Day,
            nextMinute.Hour, nextMinute.Minute, 0, DateTimeKind.Utc);
        return new DateTimeOffset(startOfNextMinute).ToUnixTimeSeconds();
    }
}
