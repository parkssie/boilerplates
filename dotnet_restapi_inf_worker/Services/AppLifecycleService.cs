namespace DotnetRestApiInfWorker.Services;

public sealed class AppLifecycleService(ILogger<AppLifecycleService> logger) : IHostedService, IDisposable
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Application started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Application stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        logger.LogInformation("Application disposed");
    }
}
