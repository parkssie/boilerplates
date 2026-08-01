using DotnetRestApiInfWorker.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace DotnetRestApiInfWorker.Data;

public sealed class Database : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public Database(AppSettings settings)
    {
        var options = settings.PostgreSql;
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = options.Host,
            Port = options.Port,
            Database = options.Database,
            Username = options.Username,
            Password = options.Password,
            SearchPath = options.SearchPath,
            SslMode = Enum.Parse<SslMode>(options.SslMode, true),
            Timeout = options.TimeoutSeconds,
            CommandTimeout = options.CommandTimeoutSeconds,
            Pooling = options.Pooling,
            MinPoolSize = options.MinPoolSize,
            MaxPoolSize = options.MaxPoolSize,
            KeepAlive = options.KeepAliveSeconds,
            ApplicationName = options.ApplicationName
        };

        foreach (var option in options.AdditionalOptions)
            connectionString[option.Key] = option.Value;

        _dataSource = NpgsqlDataSource.Create(connectionString.ConnectionString);
    }

    public async Task SaveInputAsync(string json, CancellationToken token)
    {
        await InitializeAsync(token);
        await using var command = _dataSource.CreateCommand(
            "INSERT INTO input_data (payload) VALUES (@payload)");
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, json);
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<List<(long Id, string Json)>> GetPendingResultsAsync(CancellationToken token)
    {
        await InitializeAsync(token);
        await using var command = _dataSource.CreateCommand(
            "SELECT id, payload::text FROM simulation_results " +
            "WHERE published_at IS NULL ORDER BY created_at LIMIT 100");
        await using var reader = await command.ExecuteReaderAsync(token);

        var results = new List<(long, string)>();
        while (await reader.ReadAsync(token))
            results.Add((reader.GetInt64(0), reader.GetString(1)));

        return results;
    }

    public async Task MarkPublishedAsync(long id, CancellationToken token)
    {
        await ExecuteAsync(
            "UPDATE simulation_results SET published_at = now(), last_error = NULL WHERE id = @id",
            id,
            null,
            token);
    }

    public async Task MarkFailedAsync(long id, string error, CancellationToken token)
    {
        await ExecuteAsync(
            "UPDATE simulation_results SET publish_attempts = publish_attempts + 1, last_error = @error WHERE id = @id",
            id,
            error.Length > 1000 ? error[..1000] : error,
            token);
    }

    private async Task ExecuteAsync(string sql, long id, string? error, CancellationToken token)
    {
        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        if (error is not null)
            command.Parameters.AddWithValue("error", error);
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task InitializeAsync(CancellationToken token)
    {
        if (_initialized) return;

        await _initializeLock.WaitAsync(token);
        try
        {
            if (_initialized) return;

            await using var command = _dataSource.CreateCommand(
                """
                CREATE TABLE IF NOT EXISTS input_data (
                    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    payload jsonb NOT NULL,
                    collected_at timestamptz NOT NULL DEFAULT now()
                );

                CREATE TABLE IF NOT EXISTS simulation_results (
                    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    payload jsonb NOT NULL,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    published_at timestamptz NULL,
                    publish_attempts integer NOT NULL DEFAULT 0,
                    last_error text NULL
                );
                """);
            await command.ExecuteNonQueryAsync(token);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _initializeLock.Dispose();
        await _dataSource.DisposeAsync();
    }
}
