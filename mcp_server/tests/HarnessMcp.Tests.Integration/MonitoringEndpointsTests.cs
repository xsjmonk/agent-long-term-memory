using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using HarnessMcp.Host.Aot;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HarnessMcp.Tests.Integration;

public sealed class MonitoringEndpointsTests
{
    private sealed class NoOpMonitorEventExporter : IMonitorEventExporter
    {
        public ValueTask<MonitorBatchDto> GetSinceAsync(long lastSequence, int maxCount, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new MonitorBatchDto(lastSequence, lastSequence, Array.Empty<MonitorEventDto>()));
    }

    private sealed class NoOpSnapshotService : IMonitoringSnapshotService
    {
        public ValueTask<MonitorSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            var server = new MonitorServerSummaryDto(
                ServerName: "s",
                ServerVersion: "v",
                ProtocolMode: "http",
                MonitoringEnabled: true,
                RealtimeEnabled: false,
                StartedUtc: DateTimeOffset.UtcNow,
                Environment: "env",
                DatabaseConfigured: true,
                EmbeddingProviderSummary: "local");

            return ValueTask.FromResult(new MonitorSnapshotDto(
                Server: server,
                RecentLogs: Array.Empty<MonitorEventDto>(),
                RecentOperations: Array.Empty<MonitorEventDto>(),
                RecentTimings: Array.Empty<MonitorEventDto>(),
                RecentWarnings: Array.Empty<MonitorEventDto>(),
                RecentOutputs: Array.Empty<MonitorEventDto>(),
                LastSequence: 0));
        }
    }

    private static ComposedApplication MakeComposed(AppConfig cfg)
    {
        return new ComposedApplication(
            Config: cfg,
            DataSource: null!,
            HealthProbe: null!,
            KnowledgeQueryTools: null!,
            KnowledgeResources: null!,
            MonitorEventSink: null!,
            MonitorEventExporter: new NoOpMonitorEventExporter(),
            MonitoringSnapshotService: new NoOpSnapshotService(),
            MonitorEventBroadcaster: null!,
            AppInfoProvider: null!,
            LoggerFactory: NullLoggerFactory.Instance);
    }

    private static async Task<string> GetStringAsync(System.Net.Http.HttpClient client, string url)
    {
        using var resp = await client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task MonitoringUiEnabled_ConfigDrivesMonitorRoute()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcp-host-aot-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "appsettings.mcp.json");
            File.WriteAllText(
                path,
                """
                {
                  "Server": {
                    "TransportMode": "Http",
                    "HttpListenUrl": "http://127.0.0.1:5081",
                    "EnableMonitoringUi": true,
                    "Environment": "Development"
                  },
                  "Monitoring": {
                    "EnableRealtimeUi": false
                  }
                }
                """,
                Encoding.UTF8);

            var cfg = AppConfigLoader.LoadFromPath(path, Array.Empty<string>());
            var composed = MakeComposed(cfg);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            var app = builder.Build();

            var monitoringUiEnabled = cfg.Server.EnableMonitoringUi;
            var realtimeEnabled = monitoringUiEnabled && cfg.Monitoring.EnableRealtimeUi;
            app.MapMonitoring(composed, monitoringUiEnabled, realtimeEnabled);

            await app.StartAsync();
            var client = app.GetTestClient();

            var html = await GetStringAsync(client, "/monitor");
            Assert.Contains("<title>Harness MCP Monitor</title>", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Monitoring UI disabled", html, StringComparison.OrdinalIgnoreCase);

            var eventsStatus = await client.GetAsync("/monitor/events");
            Assert.Equal(System.Net.HttpStatusCode.OK, eventsStatus.StatusCode);

            var snapshotStatus = await client.GetAsync("/monitor/snapshot");
            Assert.Equal(System.Net.HttpStatusCode.OK, snapshotStatus.StatusCode);

            var hubStatus = await client.GetAsync("/monitor/hub");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, hubStatus.StatusCode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task MonitoringUiDisabled_ConfigRemovesMonitorRoute()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcp-host-aot-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "appsettings.mcp.json");
            File.WriteAllText(
                path,
                """
                {
                  "Server": {
                    "TransportMode": "Http",
                    "HttpListenUrl": "http://127.0.0.1:5081",
                    "EnableMonitoringUi": false,
                    "Environment": "Development"
                  },
                  "Monitoring": {
                    "EnableRealtimeUi": true
                  }
                }
                """,
                Encoding.UTF8);

            var cfg = AppConfigLoader.LoadFromPath(path, Array.Empty<string>());
            var composed = MakeComposed(cfg);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            var app = builder.Build();

            var monitoringUiEnabled = cfg.Server.EnableMonitoringUi;
            var realtimeEnabled = monitoringUiEnabled && cfg.Monitoring.EnableRealtimeUi;
            app.MapMonitoring(composed, monitoringUiEnabled, realtimeEnabled);

            await app.StartAsync();
            var client = app.GetTestClient();

            var monitorStatus = await client.GetAsync("/monitor");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, monitorStatus.StatusCode);

            var eventsStatus = await client.GetAsync("/monitor/events");
            Assert.Equal(System.Net.HttpStatusCode.OK, eventsStatus.StatusCode);

            var snapshotStatus = await client.GetAsync("/monitor/snapshot");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, snapshotStatus.StatusCode);

            var hubStatus = await client.GetAsync("/monitor/hub");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, hubStatus.StatusCode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

