using Ping.Services.AI;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Ping.Services.Background;

/// <summary>
/// Singleton background service that reads <see cref="PingGenreJob"/> items
/// from an in-process channel and auto-classifies the genre of newly created
/// pings that were saved without one.
/// </summary>
public class PingGenreClassificationService(
    IServiceProvider services,
    ChannelReader<PingGenreJob> jobReader,
    ILogger<PingGenreClassificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[GenreClassifier] Background service starting.");

        try
        {
            await foreach (var job in jobReader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // Delay slightly to let the user finish typing their review/activity on the stepper,
                    // so the database has the activity record when we reload the ping.
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

                    using var scope = services.CreateScope();
                    var classifier = scope.ServiceProvider.GetRequiredService<IPingGenreClassifier>();
                    await classifier.ClassifyAsync(job, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Never let a single job failure crash the service.
                    logger.LogError(ex, "[GenreClassifier] Unhandled error classifying ping {PingId}. Job dropped.", job.PingId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("[GenreClassifier] Background service stopping.");
        }
    }
}
