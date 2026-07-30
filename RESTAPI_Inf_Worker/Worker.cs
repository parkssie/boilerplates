using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly string _apiUrl;
    private readonly string _apiToken;
    private readonly string _connectionString;

    public Worker(ILogger<Worker> logger, IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _config = config;

        _apiUrl = _config["RestApi:Url"] ?? throw new ArgumentNullException("RestApi:Url");
        _apiToken = _config["RestApi:Token"];

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = _config["Postgres:Host"] ?? "localhost",
            Port = int.TryParse(_config["Postgres:Port"], out var p) ? p : 5432,
            Username = _config["Postgres:Username"] ?? "postgres",
            Password = _config["Postgres:Password"] ?? "",
            Database = _config["Postgres:Database"] ?? "postgres",
        };
        if (!string.IsNullOrEmpty(_config["Postgres:SearchPath"]))
            builder.SearchPath = _config["Postgres:SearchPath"];

        _connectionString = builder.ConnectionString;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RESTAPI_Inf_Worker started.");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DoWorkAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during DoWork");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
        _logger.LogInformation("RESTAPI_Inf_Worker stopping.");
    }

    private async Task DoWorkAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("api");
        if (!string.IsNullOrEmpty(_apiToken))
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiToken);

        _logger.LogInformation("Calling {Url}", _apiUrl);
        using var res = await client.GetAsync(_apiUrl, ct);
        var content = await res.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("Received {Status} {Len} bytes", (int)res.StatusCode, content?.Length ?? 0);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Ensure table exists
        var createSql = @"
            CREATE TABLE IF NOT EXISTS api_responses (
              id BIGSERIAL PRIMARY KEY,
              response jsonb,
              status_code integer,
              received_at timestamptz DEFAULT now()
            );";
        await using (var createCmd = new NpgsqlCommand(createSql, conn))
            await createCmd.ExecuteNonQueryAsync(ct);

        // Insert response (store full JSON as jsonb)
        await using (var insert = new NpgsqlCommand("INSERT INTO api_responses(response, status_code) VALUES (@resp::jsonb, @status);", conn))
        {
            insert.Parameters.AddWithValue("resp", NpgsqlDbType.Jsonb, string.IsNullOrEmpty(content) ? "{}" : content);
            insert.Parameters.AddWithValue("status", (int)res.StatusCode);
            await insert.ExecuteNonQueryAsync(ct);
        }
    }
}
