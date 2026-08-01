namespace DotnetRestApiInfWorker.Services;

public sealed class AppLifecycleService(ILogger<AppLifecycleService> logger) : IHostedService, IDisposable
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("app start");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("app stop");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        logger.LogInformation("app disposed");
    }
}
