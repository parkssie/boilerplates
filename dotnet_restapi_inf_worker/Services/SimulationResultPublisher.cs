using System.Net.Http.Headers;
using System.Text;
using DotnetRestApiInfWorker.Configuration;
using DotnetRestApiInfWorker.Data;

namespace DotnetRestApiInfWorker.Services;

public sealed class SimulationResultPublisher(
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
            await PublishSimulationResultAsync(json, token);
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

    private async Task PublishSimulationResultAsync(string json, CancellationToken token)
    {
        using HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(settings.SimulationResultPublisher.TimeoutSeconds)
        };

        if (!string.IsNullOrWhiteSpace(settings.SimulationResultPublisher.Token))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.SimulationResultPublisher.Token);
        }

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(
            settings.SimulationResultPublisher.Path,
            content,
            token);
        response.EnsureSuccessStatusCode();
    }
}
