using DotnetRestApiInfWorker.Configuration;
using DotnetRestApiInfWorker.Data;

namespace DotnetRestApiInfWorker.Services;

public sealed class InputDataCollector(
    RestApiClient restApiClient,
    Database database,
    AppSettings settings,
    ILogger<InputDataCollector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var options = settings.InputDataCollector;
        if (!options.Enabled) return;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                var next = NextRun(now, options.MinuteOfHour);
                logger.LogDebug("Next input collection: {NextRun}", next);
                await Task.Delay(next - now, token);

                var json = await restApiClient.GetInputDataAsync(token);
                await database.SaveInputAsync(json, token);
                logger.LogInformation("Input data saved");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Input collection failed");
            }
        }
    }

    private static DateTimeOffset NextRun(DateTimeOffset now, int minute)
    {
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, minute, 0, now.Offset);
        return next <= now ? next.AddHours(1) : next;
    }
}
