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
    private const int TemporarySimDataSizeLimitBytes = 200;

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var options = settings.SimulationResultPublisher;
        if (!options.Enabled) return;

        if (options.RequestIntervalSec <= 0)
            throw new InvalidOperationException("RequestIntervalSec must be greater than 0");

        using HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSec)
        };

        if (!string.IsNullOrWhiteSpace(options.RequestToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.RequestToken);
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                var results = await database.LoadSimulationResultsAsync(token);
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
                await Task.Delay(TimeSpan.FromSeconds(options.RequestIntervalSec), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PublishAsync(
        HttpClient client,
        SimulationResult result,
        CancellationToken token)
    {
        try
        {
            using var content = new StringContent(result.Json, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                settings.SimulationResultPublisher.RequestPath,
                content,
                token);
            response.EnsureSuccessStatusCode();

            logger.LogInformation(
                "Simulation result published. StnCd: {StnCd}, SimCd: {SimCd}, LayoutId: {LayoutId}, NodeId: {NodeId}, FlowId: {FlowId}",
                result.StnCd,
                result.SimCd,
                result.LayoutId,
                result.NodeId,
                result.FlowId);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Simulation result publish failed. StnCd: {StnCd}, SimCd: {SimCd}, LayoutId: {LayoutId}, NodeId: {NodeId}, FlowId: {FlowId}",
                result.StnCd,
                result.SimCd,
                result.LayoutId,
                result.NodeId,
                result.FlowId);
        }
    }
}
