using System.Text;
using DotnetRestApiInfWorker.Configuration;

namespace DotnetRestApiInfWorker.Services;

public sealed class RestApiClient(HttpClient httpClient, AppSettings settings)
{
    public Task<string> GetInputDataAsync(CancellationToken token) =>
        httpClient.GetStringAsync(settings.RestApi.InputDataPath, token);

    public async Task PublishSimulationResultAsync(string json, CancellationToken token)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(
            settings.RestApi.SimulationResultPath,
            content,
            token);
        response.EnsureSuccessStatusCode();
    }
}
