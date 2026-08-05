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

public sealed record ScadaRecvConfig(string stn_cd, DateTime dt_meas);

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
/// 현재 시각 기준 1시간 전부터 10분 전까지의 데이터를 수집합니다.
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

        while (!token.IsCancellationRequested)
        {
            try
            {
                // 1. DB에서 입력 데이터 수집 설정 조회
                var inputConfigs = await database.LoadInputSettingsAsync(token);

                // 2. DB에서 마지막 수집지점 조회
                var scadaRecvConfigs = await database.LoadScadaRecvConfigAsync(token);

                // 3. REST API 호출을 위한 HttpClient 생성
                using HttpClient client = new()
                {
                    Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
                };

                // 3. 인증 토큰이 설정되어 있는 경우 Authorization 헤더에 Bearer 토큰 추가
                if (!string.IsNullOrWhiteSpace(options.Token))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", options.Token);
                }

                const string contentType = "application/json";
                const string dateTimeFormat = "yyyy-MM-dd HH:mm:ss.fff";
                var requestTime = DateTime.Now;
                var collectionStart = requestTime.AddHours(-1);
                var collectionEnd = requestTime.AddMinutes(-10);

                foreach (var inputConfig in inputConfigs)
                {
                    // 4. InputConfig별 요청 페이로드 생성

                    // 현재 발전소의 가장 최근 수집 시각을 조회하고 로컬 시각으로 변환
                    var lastMeasuredAt = scadaRecvConfigs
                        .Where(item => item.stn_cd == inputConfig.stn_cd)
                        .MaxBy(item => item.dt_meas)
                        ?.dt_meas.ToLocalTime();

                    // 최근 수집 시각이 없거나 1시간보다 오래됐으면 수집 시작 시각을 1시간 전으로 설정
                    var fromDt = lastMeasuredAt is null || lastMeasuredAt < collectionStart
                        ? collectionStart
                        : lastMeasuredAt.Value;

                    // 이미 10분 전까지 수집했다면 현재 설정의 API 요청을 생략
                    if (fromDt >= collectionEnd)
                        continue;

                    // 한 번의 요청에서 수집할 종료 시각을 시작 시각으로부터 10분 후로 설정
                    var toDt = fromDt.AddMinutes(10);

                    // 종료 시각이 현재 시각의 10분 전을 넘지 않도록 제한
                    if (toDt > collectionEnd)
                        toDt = collectionEnd;

                    // 현재 입력 설정에 포함된 SCIF 태그 주소 목록을 생성
                    var tagIds = inputConfig.inputConfigItem
                        .Select(item => item.scif_address_tag)
                        .ToList();

                    var payload = new
                    {
                        dtType = "<data-type>",
                        tagIds,
                        fromDt = fromDt.ToString(dateTimeFormat),
                        toDt = toDt.ToString(dateTimeFormat),
                        dataKind = "<data-kind>"
                    };

                    // 5. REST API 호출 및 응답 처리
                    var jsonString = JsonConvert.SerializeObject(payload);
                    using var content = new StringContent(jsonString, Encoding.UTF8, contentType);
                    using HttpResponseMessage response = await client.PostAsync(
                        options.Path,
                        content,
                        token);
                    response.EnsureSuccessStatusCode();

                    // 6. 응답 데이터를 InputData 객체로 역직렬화
                    var responseString = await response.Content.ReadAsStringAsync(token);
                    var inputData = JsonConvert.DeserializeObject<InputData>(responseString)
                        ?? throw new JsonSerializationException("Input data response is empty");

                    // 7. 결과 저장
                    await database.SaveInputAsync(inputConfig, inputData, token);
                    logger.LogInformation("Input data saved");
                }

                await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), token);
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
