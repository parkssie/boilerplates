using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BP;

public sealed class Worker(App app, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await app.StartAsync(stoppingToken);
            await app.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Worker execution was cancelled.");
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Worker stopped unexpectedly.");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker stop requested.");
        await app.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        app.Dispose();
        base.Dispose();
    }
}
