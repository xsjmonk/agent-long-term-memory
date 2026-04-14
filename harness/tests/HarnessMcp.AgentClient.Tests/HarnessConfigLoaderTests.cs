using System.IO;
using System.Text.Json;
using FluentAssertions;
using HarnessMcp.AgentClient.Config;
using HarnessMcp.AgentClient.Support;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class HarnessConfigLoaderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void config_class_has_required_properties()
    {
        var opts = new HarnessRuntimeOptions();
        opts.Mcp.Should().NotBeNull();
        opts.Model.Should().NotBeNull();
        opts.PlanningDefaults.Should().NotBeNull();
        opts.Paths.Should().NotBeNull();
    }

    [Fact]
    public void config_default_values_are_set()
    {
        var opts = new HarnessRuntimeOptions();
        opts.Mcp.TimeoutSeconds.Should().Be(120);
        opts.Model.ApiKeyEnvVar.Should().Be("OPENAI_API_KEY");
        opts.Model.TimeoutSeconds.Should().Be(180);
        opts.PlanningDefaults.MinimumAuthority.Should().Be("Reviewed");
        opts.PlanningDefaults.MaxItemsPerChunk.Should().Be(5);
        opts.PlanningDefaults.EmitIntermediates.Should().BeTrue();
        opts.PlanningDefaults.StdoutJson.Should().BeTrue();
        opts.PlanningDefaults.PrintWorkerPacket.Should().BeFalse();
        opts.Paths.DefaultOutputRoot.Should().Be(".harness/runs");
    }

    [Fact]
    public void resolve_fails_when_mcp_url_missing()
    {
        var result = HarnessConfigLoader.LoadWithConfig(new[] { "--task-text", "test" });
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MCP base URL"));
    }

    [Fact]
    public void resolve_fails_when_model_url_missing()
    {
        var result = HarnessConfigLoader.LoadWithConfig(new[] 
        { 
            "--task-text", "test", 
            "--mcp-base-url", "http://x:1" 
        });
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model base URL"));
    }

    [Fact]
    public void resolve_fails_when_model_name_missing()
    {
        var result = HarnessConfigLoader.LoadWithConfig(new[] 
        { 
            "--task-text", "test", 
            "--mcp-base-url", "http://x:1",
            "--model-base-url", "http://y:2"
        });
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Model name"));
    }

    [Fact]
    public void resolve_succeeds_with_cli_only()
    {
        var result = HarnessConfigLoader.LoadWithConfig(new[] 
        { 
            "--task-text", "test", 
            "--mcp-base-url", "http://x:1",
            "--model-base-url", "http://y:2",
            "--model-name", "model"
        });
        result.IsSuccess.Should().BeTrue();
        result.Value!.McpBaseUrl.Should().Be("http://x:1");
        result.Value.ModelBaseUrl.Should().Be("http://y:2");
        result.Value.ModelName.Should().Be("model");
    }

    [Fact]
    public void resolve_succeeds_with_task_file()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test task");
            var result = HarnessConfigLoader.LoadWithConfig(new[] 
            { 
                "--task-file", tempFile, 
                "--mcp-base-url", "http://x:1",
                "--model-base-url", "http://y:2",
                "--model-name", "model"
            });
            result.IsSuccess.Should().BeTrue();
            result.Value!.TaskFile.Should().Be(tempFile);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void resolve_fails_when_both_task_text_and_task_file()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test task");
            var result = HarnessConfigLoader.LoadWithConfig(new[] 
            { 
                "--task-text", "test",
                "--task-file", tempFile,
                "--mcp-base-url", "http://x:1",
                "--model-base-url", "http://y:2",
                "--model-name", "model"
            });
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("Exactly one"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}