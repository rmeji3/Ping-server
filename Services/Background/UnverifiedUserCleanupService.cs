using Conquest.Data.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.Background;

public class UnverifiedUserCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<UnverifiedUserCleanupService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);
    private readonly TimeSpan _expirationAge = TimeSpan.FromHours(12);

    public UnverifiedUserCleanupService(
        IServiceProvider services, 
        ILogger<UnverifiedUserCleanupService> logger)
    {
        _services = services;
        _logger = logger;
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
