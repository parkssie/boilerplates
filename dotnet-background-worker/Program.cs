using BP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;

LogManager.Setup().LoadConfigurationFromFile("nlog.config");

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
builder.Logging.AddNLog();

builder.Services.AddWindowsService(options => options.ServiceName = "BP.BackgroundWorker");
builder.Services.AddSystemd();
builder.Services.AddSingleton<App>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("HostLifetime");
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

lifetime.ApplicationStarted.Register(() => logger.LogInformation("Host started."));
lifetime.ApplicationStopping.Register(() => logger.LogInformation("Host is stopping."));
lifetime.ApplicationStopped.Register(() => logger.LogInformation("Host stopped."));

try
{
    await host.RunAsync();
}
finally
{
    host.Dispose();
    LogManager.Shutdown();
}
