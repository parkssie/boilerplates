using System.Globalization;
using DotnetRestApiInfWorker.Configuration;
using DotnetRestApiInfWorker.Services;
using Npgsql;

namespace DotnetRestApiInfWorker.Data;

public sealed class Database : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

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

    public async Task<Dictionary<string, DateTime>> LoadLastReceivedDtAsync(
        CancellationToken token)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT
                stn_cd,
                MAX(dt) AS max_dt
            FROM public.data_scada_raw
            GROUP BY stn_cd
            """);
        await using var reader = await command.ExecuteReaderAsync(token);

        var lastReceivedByStation = new Dictionary<string, DateTime>();
        while (await reader.ReadAsync(token))
        {
            lastReceivedByStation.Add(
                reader.GetString(0),
                reader.GetDateTime(1));
        }

        return lastReceivedByStation;
    }

    public async Task SaveInputAsync(
        InputConfig inputConfig,
        InputData inputData,
        CancellationToken token)
    {
        // 1. REST 응답을 측정 시각별 DB 저장 값으로 변환
        var valuesByMeasuredAt = BuildScadaValuesByMeasuredAt(
            inputConfig,
            inputData,
            out var firstMeasuredAt);

        // 2. 저장 가능한 데이터가 없으면 종료
        if (valuesByMeasuredAt.Count == 0)
            return;

        // 3. 원시 데이터와 수신 이력을 하나의 트랜잭션으로 처리
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);

        // 4. 측정 시각별 data_scada_raw 행 저장
        foreach (var (measuredAt, valuesByColumn) in valuesByMeasuredAt)
            await UpsertScadaRawAsync(
                connection,
                transaction,
                inputConfig,
                measuredAt,
                valuesByColumn,
                token);

        // 5. 최초 수신 측정 시각을 log_scada_recv에 기록
        if (firstMeasuredAt is { } firstReceivedAt)
            await UpsertScadaReceiveLogAsync(
                connection,
                transaction,
                inputConfig.stn_cd,
                firstReceivedAt,
                token);

        // 6. 전체 저장 결과 확정
        await transaction.CommitAsync(token);
    }

    private static Dictionary<DateTime, Dictionary<string, double>>
        BuildScadaValuesByMeasuredAt(
            InputConfig inputConfig,
            InputData inputData,
            out DateTime? firstMeasuredAt)
    {
        // 1. scif_address_tag 기준 설정 항목 인덱스 생성
        var configItemsByScifTag = new Dictionary<string, InputConfigItem>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var configItem in inputConfig.inputConfigItem)
            configItemsByScifTag.TryAdd(configItem.scif_address_tag, configItem);

        // 2. 측정 시각별 [col_cd: value] 저장소 초기화
        firstMeasuredAt = null;
        var valuesByMeasuredAt = new Dictionary<DateTime, Dictionary<string, double>>();
        using var commandBuilder = new NpgsqlCommandBuilder();

        // 3. REST 응답 항목별 저장 값 변환
        foreach (var result in inputData.Data?.LstDtRst ?? [])
        {
            // 3-1. 태그 또는 측정 시각이 없는 응답 제외
            if (result?.TagId is null || result.TagDt is null)
                continue;

            // 3-2. TagId와 일치하는 scif_address_tag 설정 조회
            if (!configItemsByScifTag.TryGetValue(result.TagId, out var configItem))
                continue;

            // 3-3. TagDt 문자열을 측정 시각으로 변환
            if (!DateTime.TryParse(
                    result.TagDt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var measuredAt))
            {
                continue;
            }

            // 3-4. 데이터 유형이 LAST이면 최종값, 그 외에는 평균값 선택
            var valueText = string.Equals(
                configItem.scif_address_data_type,
                "LAST",
                StringComparison.OrdinalIgnoreCase)
                ? result.TagLastVal
                : result.TagAvgVal;

            // 3-5. 선택한 측정값을 숫자로 변환
            if (!double.TryParse(
                    valueText,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                continue;
            }

            // 3-6. 측정 시각 UTC 변환 및 col_cd 식별자 quoting
            var measuredAtUtc = measuredAt.Kind == DateTimeKind.Utc
                ? measuredAt
                : measuredAt.ToUniversalTime();
            var columnName = commandBuilder.QuoteIdentifier(configItem.col_cd);

            // 3-7. 동일한 측정 시각의 태그 값을 하나의 DB 행으로 구성
            if (!valuesByMeasuredAt.TryGetValue(measuredAtUtc, out var valuesByColumn))
            {
                valuesByColumn = new Dictionary<string, double>(StringComparer.Ordinal);
                valuesByMeasuredAt.Add(measuredAtUtc, valuesByColumn);
            }

            // 3-8. col_cd 대상 컬럼에 측정값 할당
            valuesByColumn[columnName] = value;
            firstMeasuredAt ??= measuredAtUtc;
        }

        // 4. 측정 시각별 DB 저장 값 반환
        return valuesByMeasuredAt;
    }

    private static async Task UpsertScadaRawAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        InputConfig inputConfig,
        DateTime measuredAt,
        Dictionary<string, double> valuesByColumn,
        CancellationToken token)
    {
        // 1. 동적 INSERT 대상 col_cd 컬럼 정렬
        var columns = valuesByColumn.Keys
            .Order(StringComparer.Ordinal)
            .ToList();

        // 2. 컬럼별 값 파라미터 생성
        var valueParameters = columns
            .Select((_, index) => $"@value{index}")
            .ToList();

        // 3. 충돌 시 갱신할 col_cd 구문 생성
        var updateAssignments = columns
            .Select(column => $"{column} = EXCLUDED.{column}")
            .ToList();

        // 4. stn_cd/dev_cd/grp_cd/dt 기준 data_scada_raw upsert SQL 구성
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            INSERT INTO data_scada_raw
                (stn_cd, dev_cd, grp_cd, dt, {string.Join(", ", columns)})
            VALUES
                (@stn_cd, @dev_cd, @grp_cd, @dt, {string.Join(", ", valueParameters)})
            ON CONFLICT (dt, grp_cd, dev_cd, stn_cd)
            DO UPDATE SET {string.Join(", ", updateAssignments)}, dt_update = now()
            """;

        // 5. 행 식별값 파라미터 설정
        command.Parameters.AddWithValue("stn_cd", inputConfig.stn_cd);
        command.Parameters.AddWithValue("dev_cd", inputConfig.dev_cd);
        command.Parameters.AddWithValue("grp_cd", inputConfig.grp_cd);
        command.Parameters.AddWithValue("dt", measuredAt);

        // 6. col_cd별 측정값 파라미터 설정
        for (var index = 0; index < columns.Count; index++)
            command.Parameters.AddWithValue($"value{index}", valuesByColumn[columns[index]]);

        // 7. 원시 데이터 저장 실행
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task UpsertScadaReceiveLogAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string stnCd,
        DateTime measuredAt,
        CancellationToken token)
    {
        // 1. stn_cd와 최초 측정 시각 기준 수신 이력 upsert SQL 구성
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO log_scada_recv (stn_cd, dt_meas, dt_update)
            VALUES (@stn_cd, @dt_meas, now())
            ON CONFLICT (stn_cd, dt_meas)
            DO UPDATE SET dt_update = now()
            """;

        // 2. 수신 이력 파라미터 설정
        command.Parameters.AddWithValue("stn_cd", stnCd);
        command.Parameters.AddWithValue("dt_meas", measuredAt);

        // 3. 수신 이력 저장 실행
        await command.ExecuteNonQueryAsync(token);
    }

    #endregion

    #region Simulation

    public async Task<List<(long Id, string Json)>> GetPendingResultsAsync(CancellationToken token)
    {
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

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
    }
}
