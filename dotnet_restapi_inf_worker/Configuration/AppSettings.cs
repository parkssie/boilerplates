namespace DotnetRestApiInfWorker.Configuration;

public sealed class AppSettings
{
    public RestApiSettings RestApi { get; set; } = new();
    public PostgreSqlSettings PostgreSql { get; set; } = new();
    public CollectorSettings InputDataCollector { get; set; } = new();
    public PublisherSettings SimulationResultPublisher { get; set; } = new();
}

public sealed class RestApiSettings
{
    public string Scheme { get; set; } = "https";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 443;
    public string Token { get; set; } = "";
    public string InputDataPath { get; set; } = "/api/input-data";
    public string SimulationResultPath { get; set; } = "/api/simulation-results";
    public int TimeoutSeconds { get; set; } = 30;
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

public sealed class CollectorSettings
{
    public bool Enabled { get; set; } = true;
    public int MinuteOfHour { get; set; } = 10;
}

public sealed class PublisherSettings
{
    public bool Enabled { get; set; } = true;
    public int ItervalSeconds { get; set; } = 30;
}
