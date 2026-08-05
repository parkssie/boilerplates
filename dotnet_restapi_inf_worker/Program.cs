using DotnetRestApiInfWorker.Configuration;
using DotnetRestApiInfWorker.Data;
using DotnetRestApiInfWorker.Services;
using NLog.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "DotnetRestApiInfWorker");

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Trace);
builder.Logging.AddNLog(Path.Combine(AppContext.BaseDirectory, "NLog.config"));

var settings = builder.Configuration.Get<AppSettings>() ?? new AppSettings();
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<Database>();

builder.Services.AddHostedService<AppLifecycleService>();
builder.Services.AddHostedService<InputDataCollector>();
builder.Services.AddHostedService<SimulationResultPublisher>();

using var host = builder.Build();
await host.RunAsync();
