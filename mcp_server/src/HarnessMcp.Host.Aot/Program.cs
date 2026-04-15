using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using HarnessMcp.Host.Aot;
using HarnessMcp.Infrastructure.Postgres;
using HarnessMcp.Transport.Mcp;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using System.IO;

var exitCode = Run(args);
Environment.Exit(exitCode);

static int Run(string[] args)
{
    var configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "appsettings.mcp.json"));

    AppConfig config;
    try
    {
        config = AppConfigLoader.Load(args);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: Failed to load config '{configPath}'. {ex.GetType().FullName}: {ex.Message}");
        return 1;
    }

    if (config.Server.IsStdio() && config.Server.EnableMonitoringUi)
        Console.Error.WriteLine("WARN: EnableMonitoringUi is ignored in Stdio transport; monitoring UI disabled.");

    if (config.Server.IsStdio())
    {
        config.Server.EnableMonitoringUi = false;
        config.Monitoring.EnableRealtimeUi = false;
        config.Logging.ForwardToMonitor = false;
    }

    var composed = CompositionRoot.Build(config);

    if (config.Server.IsHttp())
    {
        RunHttp(args, composed, configPath);
        return 0;
    }

    RunStdio(args, composed);
    return 0;
}

static void RunHttp(string[] args, ComposedApplication composed, string configPath)
{
    var builder = WebApplication.CreateSlimBuilder(args);
    builder.WebHost.UseUrls(composed.Config.Server.HttpListenUrl);

    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(LogLevel.Information);
    builder.Logging.AddConsole();

    // Rotating file logging (file size based).
    var roller = new LogFileRoller(
        composed.Config.Logging.Directory,
        composed.Config.Logging.FileNamePrefix,
        composed.Config.Logging.MaxFileSizeBytes,
        composed.Config.Logging.MaxRetainedFiles);
    var fileFormatter = new LogEventFormatter();
    builder.Logging.AddProvider(new RotatingFileLoggerProvider(roller, fileFormatter));

    var monitoringUi = composed.Config.Server.EnableMonitoringUi;
    var enableRealtimeUi = composed.Config.Monitoring.EnableRealtimeUi;
    var realtime = monitoringUi && enableRealtimeUi;

    if (composed.Config.Logging.ForwardToMonitor)
    {
        var ui = new UiLogProjector(composed.Config.Monitoring.MaxPayloadPreviewChars);
        builder.Logging.AddProvider(new MonitorAwareLoggerProvider(() => true, composed.MonitorEventSink, ui));
    }

    if (realtime)
    {
        builder.Services
            .AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.PayloadSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                options.PayloadSerializerOptions.TypeInfoResolverChain.Clear();
                options.PayloadSerializerOptions.TypeInfoResolverChain.Add(SignalRJsonSerializerContext.Default);
            });
    }

    builder.Services.AddMcpServer()
        .WithHttpTransport(o =>
        {
            o.Stateless = true;
        })
        .WithTools(composed.KnowledgeQueryTools, AppJsonSerializerContext.Default.Options)
        .WithResources(composed.KnowledgeResources);

    var app = builder.Build();

    if (monitoringUi && realtime)
        (composed.MonitorEventBroadcaster as MonitorEventBroadcaster)?.Attach(app.Services.GetRequiredService<IHubContext<MonitoringHub>>());

    app.MapMcp();

    app.MapGet("/healthz", async (CancellationToken ct) =>
    {
        var r = await composed.HealthProbe.CheckAsync(ct).ConfigureAwait(false);
        if (!r.IsHealthy)
            composed.MonitorEventSink.Publish(new MonitorEventDto(
                0,
                DateTimeOffset.UtcNow,
                MonitorEventKind.HealthFailure,
                null,
                "healthz",
                null,
                "Error",
                r.Message ?? "unhealthy",
                null));
        return r.IsHealthy 
            ? Results.Json(r, AppJsonSerializerContext.Default.HealthProbeResult) 
            : Results.Json(r, AppJsonSerializerContext.Default.HealthProbeResult, statusCode: 503);
    });

    app.MapGet("/readyz", () => Results.Json(
        new ReadyResponseDto { Ready = true },
        TransportJsonSerializerContext.Default.ReadyResponseDto));

    app.MapGet("/version", () =>
        Results.Json(composed.AppInfoProvider.GetServerInfo(), AppJsonSerializerContext.Default.ServerInfoResponse));

    app.MapMonitoring(composed, monitoringUi, realtime);

    Console.WriteLine(
        $"[monitoring] config='{configPath}' transport='{composed.Config.Server.TransportMode}' enableMonitoringUi={monitoringUi} enableRealtimeUi={enableRealtimeUi} " +
        $"routes: /monitor={(monitoringUi ? "mapped" : "not-mapped")} /monitor/hub={(realtime ? "mapped" : "not-mapped")} listen='{composed.Config.Server.HttpListenUrl}'.");

    app.Run();
}

static void RunStdio(string[] args, ComposedApplication composed)
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => { o.LogToStandardErrorThreshold = LogLevel.Trace; });

    var roller = new LogFileRoller(
        composed.Config.Logging.Directory,
        composed.Config.Logging.FileNamePrefix,
        composed.Config.Logging.MaxFileSizeBytes,
        composed.Config.Logging.MaxRetainedFiles);
    var fileFormatter = new LogEventFormatter();
    builder.Logging.AddProvider(new RotatingFileLoggerProvider(roller, fileFormatter));

    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithTools(composed.KnowledgeQueryTools, AppJsonSerializerContext.Default.Options)
        .WithResources(composed.KnowledgeResources);

    builder.Build().Run();
}
