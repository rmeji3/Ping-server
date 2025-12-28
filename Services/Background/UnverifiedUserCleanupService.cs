using Ping.Data.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Ping.Services.Background;

public class UnverifiedUserCleanupService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<UnverifiedUserCleanupService> _logger;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _expirationAge;
    private readonly IServiceProvider _services;

    public UnverifiedUserCleanupService(
        IServiceProvider services, 
        ILogger<UnverifiedUserCleanupService> logger,
        IConfiguration config)
    {
        _services = services;
        _logger = logger;
        _config = config;

        var expirationMinutes = _config.GetValue<int>("Cleanup:UnverifiedUserExpirationMinutes", 720);
        _expirationAge = TimeSpan.FromMinutes(expirationMinutes);

        var checkMinutes = _config.GetValue<int>("Cleanup:CheckIntervalMinutes", 60);
        _checkInterval = TimeSpan.FromMinutes(checkMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Unverified User Cleanup Service starting.");

        using var timer = new PeriodicTimer(_checkInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CleanupUnverifiedUsersAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Unverified User Cleanup Service is stopping.");
        }
    }

    private async Task CleanupUnverifiedUsersAsync(CancellationToken token)
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

            var expirationThreshold = DateTimeOffset.UtcNow.Subtract(_expirationAge);

            // Using ExecuteDeleteAsync for efficiency (EF Core 7+)
            var deletedCount = await db.Users
                .Where(u => !u.EmailConfirmed && u.CreatedUtc < expirationThreshold)
                .ExecuteDeleteAsync(token);

            if (deletedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} unverified user accounts created before {Threshold}.", 
                    deletedCount, expirationThreshold);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during unverified user cleanup.");
        }
    }
}

