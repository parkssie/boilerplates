using System.Globalization;
using DotnetRestApiInfWorker.Configuration;
using DotnetRestApiInfWorker.Services;
using Npgsql;

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

    #region Input

    public async Task<List<InputConfig>> LoadInputSettingsAsync(CancellationToken token)
    {
        await InitializeAsync(token);

        await using var command = _dataSource.CreateCommand(
            """
            SELECT stn_cd, dev_cd, grp_cd, tag_cd, col_cd, scif_address
            FROM cfg_scada_item
            WHERE actv = true
            ORDER BY stn_cd, dev_cd, grp_cd, tag_cd
            """);
        await using var reader = await command.ExecuteReaderAsync(token);

        var configs = new List<InputConfig>();
        var configsByKey = new Dictionary<(string StnCd, string DevCd, string GrpCd), InputConfig>();

        while (await reader.ReadAsync(token))
        {
            var stnCd = reader.GetString(0);
            var devCd = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var grpCd = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var tagCd = reader.GetString(3);
            var colCd = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            var scifAddress = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            var addressParts = scifAddress.Split('|', 2, StringSplitOptions.TrimEntries);

            var key = (stnCd, devCd, grpCd);
            if (!configsByKey.TryGetValue(key, out var config))
            {
                config = new InputConfig(stnCd, devCd, grpCd, []);
                configsByKey.Add(key, config);
                configs.Add(config);
            }

            config.inputConfigItem.Add(new InputConfigItem(
                tagCd,
                colCd,
                addressParts[0],
                addressParts.Length > 1 ? addressParts[1] : string.Empty));
        }

        return configs;
    }

    public async Task<List<ScadaRecvConfig>> LoadScadaRecvConfigAsync(CancellationToken token)
    {
        await InitializeAsync(token);

        await using var command = _dataSource.CreateCommand(
            """
            SELECT stn_cd, dt_meas
            FROM log_scada_recv
            ORDER BY stn_cd, dt_meas
            """);
        await using var reader = await command.ExecuteReaderAsync(token);

        var configs = new List<ScadaRecvConfig>();
        while (await reader.ReadAsync(token))
        {
            configs.Add(new ScadaRecvConfig(
                reader.GetString(0),
                reader.GetDateTime(1)));
        }

        return configs;
    }

    public async Task SaveInputAsync(
        InputConfig inputConfig,
        InputData inputData,
        CancellationToken token)
    {
        await InitializeAsync(token);

        var results = inputData.Data?.LstDtRst ?? [];
        DateTime? firstMeasuredAt = null;

        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);

        foreach (var result in results)
        {
            if (result?.TagId is null || result.TagDt is null)
                continue;

            var configItem = inputConfig.inputConfigItem.FirstOrDefault(item =>
                string.Equals(item.scif_address_tag, result.TagId, StringComparison.OrdinalIgnoreCase));
            if (configItem is null)
                continue;

            var columnName = GetScadaColumnName(configItem.col_cd);
            if (!DateTime.TryParse(
                    result.TagDt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var measuredAt))
            {
                continue;
            }

            var valueText = string.Equals(
                configItem.scif_address_data_type,
                "LAST",
                StringComparison.OrdinalIgnoreCase)
                ? result.TagLastVal
                : result.TagAvgVal;

            if (!double.TryParse(
                    valueText,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                continue;
            }

            var measuredAtUtc = measuredAt.Kind == DateTimeKind.Utc
                ? measuredAt
                : measuredAt.ToUniversalTime();

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                INSERT INTO data_scada_raw (stn_cd, dev_cd, grp_cd, dt, {columnName})
                VALUES (@stn_cd, @dev_cd, @grp_cd, @dt, @value)
                ON CONFLICT (dt, grp_cd, dev_cd, stn_cd)
                DO UPDATE SET {columnName} = EXCLUDED.{columnName}, dt_update = now()
                """;
            command.Parameters.AddWithValue("stn_cd", inputConfig.stn_cd);
            command.Parameters.AddWithValue("dev_cd", inputConfig.dev_cd);
            command.Parameters.AddWithValue("grp_cd", inputConfig.grp_cd);
            command.Parameters.AddWithValue("dt", measuredAtUtc);
            command.Parameters.AddWithValue("value", value);
            await command.ExecuteNonQueryAsync(token);

            firstMeasuredAt ??= measuredAtUtc;
        }

        if (firstMeasuredAt is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO log_scada_recv (stn_cd, dt_meas, dt_update)
                VALUES (@stn_cd, @dt_meas, now())
                ON CONFLICT (stn_cd, dt_meas)
                DO UPDATE SET dt_update = now()
                """;
            command.Parameters.AddWithValue("stn_cd", inputConfig.stn_cd);
            command.Parameters.AddWithValue("dt_meas", firstMeasuredAt.Value);
            await command.ExecuteNonQueryAsync(token);
        }

        await transaction.CommitAsync(token);
    }

    private static string GetScadaColumnName(string columnName)
    {
        var normalizedName = columnName.ToLowerInvariant();
        var isValid = normalizedName.Length == 5
            && normalizedName.StartsWith("it", StringComparison.Ordinal)
            && int.TryParse(
                normalizedName.AsSpan(2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var columnNumber)
            && columnNumber is >= 0 and <= 599;

        return isValid
            ? normalizedName
            : throw new InvalidOperationException(
                $"Invalid data_scada_raw column name: {columnName}");
    }

    #endregion

    #region Simulation

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

    #endregion

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
