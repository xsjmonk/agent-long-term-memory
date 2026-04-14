using System;
using System.IO;
using System.Text;
using HarnessMcp.Contracts;
using HarnessMcp.Host.Aot;
using Xunit;

namespace HarnessMcp.Tests.Unit;

public sealed class MonitoringUiConfigTests
{
    [Fact]
    public void LoadFromPath_PascalCaseConfigBindsCorrectly_WithCommentsAndTrailingCommas()
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
                  // comment: config loading must skip comments
                  "Server": {
                    "TransportMode": "Http",
                    "HttpListenUrl": "http://127.0.0.1:9999",
                    "EnableMonitoringUi": true,
                    "Environment": "Development"
                  },
                  "Monitoring": {
                    "EnableRealtimeUi": true
                  },
                }
                """,
                Encoding.UTF8);

            var cfg = AppConfigLoader.LoadFromPath(path, Array.Empty<string>());

            Assert.Equal(TransportMode.Http, cfg.Server.TransportMode);
            Assert.True(cfg.Server.EnableMonitoringUi);
            Assert.True(cfg.Monitoring.EnableRealtimeUi);
            Assert.Equal("http://127.0.0.1:9999", cfg.Server.HttpListenUrl);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadFromPath_CamelCaseConfigAlsoBindsCorrectly()
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
                  "server": {
                    "transportMode": "Http",
                    "httpListenUrl": "http://127.0.0.1:9998",
                    "enableMonitoringUi": true,
                    "environment": "Development"
                  },
                  "monitoring": {
                    "enableRealtimeUi": true,
                  },
                }
                """,
                Encoding.UTF8);

            var cfg = AppConfigLoader.LoadFromPath(path, Array.Empty<string>());

            Assert.Equal(TransportMode.Http, cfg.Server.TransportMode);
            Assert.True(cfg.Server.EnableMonitoringUi);
            Assert.True(cfg.Monitoring.EnableRealtimeUi);
            Assert.Equal("http://127.0.0.1:9998", cfg.Server.HttpListenUrl);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadFromPath_CommandLineTransportOverrideStillWorks()
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
                    "HttpListenUrl": "http://127.0.0.1:9999",
                    "EnableMonitoringUi": true,
                    "Environment": "Development"
                  }
                }
                """,
                Encoding.UTF8);

            var cfg = AppConfigLoader.LoadFromPath(path, new[] { "--transport", "Stdio" });
            Assert.Equal(TransportMode.Stdio, cfg.Server.TransportMode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StaticMonitoringPageProvider_IsBrightNeutral()
    {
        var html = StaticMonitoringPageProvider.GetHtml(new AppConfig());

        Assert.DoesNotContain("#0b0f14", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#111827", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#0f172a", html, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("background: #fff", html, StringComparison.OrdinalIgnoreCase);
    }
}

