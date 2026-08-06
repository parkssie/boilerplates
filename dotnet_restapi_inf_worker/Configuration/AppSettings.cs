namespace DotnetRestApiInfWorker.Configuration;

public sealed class AppSettings
{
    public CollectorSettings InputDataCollector { get; set; } = new();
    public PublisherSettings SimulationResultPublisher { get; set; } = new();
    public PostgreSqlSettings PostgreSql { get; set; } = new();
}

public sealed class CollectorSettings
{
    public bool Enabled { get; set; } = true;
    public int PayloadToDtDelayMin { get; set; } = 30; // toDt의 delay (예: 현재 13:10 이고 30m 이면 toDt를 12:40 으로)
    public int PayloadDtRangeMin { get; set; } = 10; // fromDt와 toDt 를 나눠서 API 호출하기 위한 기준변수 (min)
    public string PayloadDtType { get; set; } = "<data-type>";
    public string PayloadDataKind { get; set; } = "<data-kind>";
    public string RequestPath { get; set; } = "https://localhost:443/api/input-data";
    public string RequestToken { get; set; } = "";
    public int RequestIntervalSec { get; set; } = 600; // 10 minutes
    public int RequestTimeoutSec { get; set; } = 30;
}

public sealed class PublisherSettings
{
    public bool Enabled { get; set; } = true;
    public int ItervalSeconds { get; set; } = 30;
    public string RequestPath { get; set; } = "https://localhost:443/api/simulation-results";
    public string RequestToken { get; set; } = "";
    public int RequestTimeoutSec { get; set; } = 30;
}

public sealed class PostgreSqlSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "simulation";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "";
    public string SearchPath { get; set; } = "public";
    public string SslMode { get; set; } = "Prefer";
    public int TimeoutSeconds { get; set; } = 15;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public bool Pooling { get; set; } = true;
    public int MinPoolSize { get; set; }
    public int MaxPoolSize { get; set; } = 100;
    public int KeepAliveSeconds { get; set; } = 30;
    public string ApplicationName { get; set; } = "DotnetRestApiInfWorker";
    public Dictionary<string, string> AdditionalOptions { get; set; } = [];
}
