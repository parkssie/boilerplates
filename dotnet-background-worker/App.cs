using Microsoft.Extensions.Logging;

namespace BP;

public sealed class App(ILogger<App> logger) : IDisposable
{
    private readonly CancellationTokenSource _applicationStopping = new();
    private bool _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation("Application started.");
        return Task.CompletedTask;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        ThrowIfDisposed();

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            _applicationStopping.Token);
        var cancellationToken = linkedCancellation.Token;

        while (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Application is running.");

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The host requested a normal shutdown.
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        logger.LogInformation("Application stopping.");
        _applicationStopping.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        logger.LogInformation("Application disposing. Releasing managed resources.");
        _applicationStopping.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
