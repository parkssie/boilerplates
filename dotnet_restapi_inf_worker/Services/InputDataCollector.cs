using System.Net.Http.Headers;
using System.Text;
using DotnetRestApiInfWorker.Configuration;
using DotnetRestApiInfWorker.Data;
using Newtonsoft.Json;

namespace DotnetRestApiInfWorker.Services;

#region Input configuration models

public sealed record InputConfigItem(
    string tag_cd,
    string col_cd,
    string scif_address_tag,
    string scif_address_data_type);

public sealed record InputConfig(
    string stn_cd,
    string dev_cd,
    string grp_cd,
    List<InputConfigItem> inputConfigItem);

#endregion

#region Input data response models

public sealed record LstDtRst(
    string? TagId,
    string? TagDt,
    string? TagLastVal,
    string? TagAvgVal,
    string? MeasValChar);

public sealed record Data(string ExecId, LstDtRst?[] LstDtRst);

public sealed record InputData(
    string Status,
    Data? Data,
    string? ErrorMessage,
    string? ErrorCode);

#endregion

/// <summary>
/// REST API에서 입력 데이터를 주기적으로 수집하여 데이터베이스에 저장합니다.
/// </summary>
/// <remarks>
/// 마지막 측정 시각의 1분 후부터 현재 시각 기준 설정된 지연 시간 전까지 데이터를 수집합니다.
/// </remarks>
public sealed class InputDataCollector(
    Database database,
    AppSettings settings,
    ILogger<InputDataCollector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var options = settings.InputDataCollector;
        if (!options.Enabled) return;

        if (options.PayloadDtRangeMin <= 0)
            throw new InvalidOperationException("PayloadDtRangeMin must be greater than 0");

        const string contentType = "application/json";
        const string dateTimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

        while (!token.IsCancellationRequested)
        {
            try
            {
                // 1. DB에서 입력 데이터 수집 설정 조회
                var inputConfigs = await database.LoadInputSettingsAsync(token);

                // 2. DB에서 마지막 수집지점 조회
                var lastReceivedDtByStation = await database.LoadLastReceivedDtAsync(token);

                // 3. REST API 호출을 위한 HttpClient 생성
                using HttpClient client = new()
                {
                    Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSec)
                };

                // 4. 인증 토큰이 설정되어 있는 경우 Authorization 헤더에 Bearer 토큰 추가
                if (!string.IsNullOrWhiteSpace(options.RequestToken))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", options.RequestToken);
                }

                var now = DateTime.Now;
                var toDt = now.AddMinutes(-options.PayloadToDtDelayMin);

                foreach (var inputConfig in inputConfigs)
                {
                    // 4. InputConfig별 요청 페이로드 생성

                    // 마지막 측정 시각이 있으면 그다음 1분부터, 없으면 1시간 전부터 수집
                    var fromDt = lastReceivedDtByStation.TryGetValue(
                        inputConfig.stn_cd,
                        out var lastReceivedAt)
                        ? lastReceivedAt.ToLocalTime().AddMinutes(1)
                        : now.AddHours(-1);

                    // 수집 가능한 종료 시각을 넘었으면 현재 설정의 API 요청을 생략
                    if (fromDt > toDt)
                        continue;

                    // 현재 입력 설정에 포함된 SCIF 태그 주소 목록을 생성
                    var tagIds = inputConfig.inputConfigItem
                        .Select(item => item.scif_address_tag)
                        .ToList();

                    var requestFromDt = fromDt;
                    while (requestFromDt <= toDt)
                    {
                        var requestToDt = requestFromDt.AddMinutes(options.PayloadDtRangeMin);
                        if (requestToDt > toDt)
                            requestToDt = toDt;

                        var payload = new
                        {
                            dtType = options.PayloadDtType,
                            tagIds,
                            fromDt = requestFromDt.ToString(dateTimeFormat),
                            toDt = requestToDt.ToString(dateTimeFormat),
                            dataKind = options.PayloadDataKind
                        };

                        var logDtFrom = DateTime.Now;

                        // 5. REST API 호출 및 응답 처리
                        var jsonString = JsonConvert.SerializeObject(payload);
                        using var content = new StringContent(jsonString, Encoding.UTF8, contentType);
                        using HttpResponseMessage response = await client.PostAsync(
                            options.RequestPath,
                            content,
                            token);
                        response.EnsureSuccessStatusCode();

                        // 6. 응답 데이터를 InputData 객체로 역직렬화
                        var responseString = await response.Content.ReadAsStringAsync(token);
                        var inputData = JsonConvert.DeserializeObject<InputData>(responseString)
                            ?? throw new JsonSerializationException("Input data response is empty");

                        // 로그
                        var logPayload = JsonConvert.SerializeObject(new
                        {
                            payload.dtType,
                            tagIdCount = tagIds.Count,
                            payload.fromDt,
                            payload.toDt,
                            payload.dataKind
                        });

                        var requestElapsedSec = (DateTime.Now - logDtFrom).TotalSeconds;
                        logger.LogInformation(
                            "Input data received. StnCd: {StnCd}, RequestElapsedSec: {RequestElapsedSec:F3}, Payload: {Payload}",
                            inputConfig.stn_cd,
                            requestElapsedSec,
                            logPayload);

                        // 7. 결과 저장
                        await database.SaveInputAsync(inputConfig, inputData, token);
                        logger.LogInformation("Input data saved");

                        if (requestToDt >= toDt)
                            break;

                        // 구간 경계 시각이 누락되지 않도록 직전 종료 시각부터 이어서 수집
                        requestFromDt = requestToDt;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(options.RequestIntervalSec), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Input collection failed: {ErrorMessage}. Retrying in 5 seconds",
                    exception.Message);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
