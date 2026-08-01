using DotnetRestApiInfWorker.Configuration;
using DotnetRestApiInfWorker.Data;

namespace DotnetRestApiInfWorker.Services;

public sealed class SimulationResultPublisher(
    RestApiClient restApiClient,
    Database database,
    AppSettings settings,
    ILogger<SimulationResultPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var options = settings.SimulationResultPublisher;
        if (!options.Enabled) return;

        while (!token.IsCancellationRequested)
        {
            try
            {
                foreach (var result in await database.GetPendingResultsAsync(token))
                    await PublishAsync(result.Id, result.Json, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Simulation result query failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.ItervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PublishAsync(long id, string json, CancellationToken token)
    {
        try
        {
            await restApiClient.PublishSimulationResultAsync(json, token);
            await database.MarkPublishedAsync(id, token);
            logger.LogInformation("Simulation result {ResultId} published", id);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Simulation result {ResultId} publish failed", id);
            await database.MarkFailedAsync(id, exception.Message, token);
        }
    }
}
