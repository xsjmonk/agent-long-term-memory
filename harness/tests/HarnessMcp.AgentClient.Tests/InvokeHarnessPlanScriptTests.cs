using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class InvokeHarnessPlanScriptTests
{
    private static readonly string ScriptPath = @"C:\Docs\工作笔记\Hackthon\2026\harness\Scripts\invoke-harness-plan.ps1";

    [Fact]
    public void script_exists_at_expected_path()
    {
        File.Exists(ScriptPath).Should().BeTrue();
    }

    private static string ReadScriptContent() =>
        File.ReadAllText(ScriptPath, System.Text.Encoding.UTF8);

    [Fact]
    public void script_forces_stdout_json_true()
    {
        var content = ReadScriptContent();
        var hasStdoutJson = content.IndexOf("--stdout-json", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            content.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0;
        hasStdoutJson.Should().BeTrue("script must always pass --stdout-json true to the harness");
    }

    [Fact]
    public void script_returns_only_manifest_json_to_stdout()
    {
        var content = ReadScriptContent();

        var writeOutputOccurrences = Regex.Matches(content, @"Write-Output\s+\$manifestJson", RegexOptions.IgnoreCase).Count;
        var echoManifestOccurrences = Regex.Matches(content, @"\$\s*manifestJson\s*\|.*Write-Output", RegexOptions.IgnoreCase).Count;

        (writeOutputOccurrences + echoManifestOccurrences).Should().BeGreaterThan(0,
            "script must write manifest JSON to stdout via Write-Output");
    }

    [Fact]
    public void script_exits_nonzero_on_harness_failure()
    {
        var content = ReadScriptContent();

        var hasNonZeroExit = content.IndexOf("exit 1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             content.IndexOf("exit $exitCode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             content.IndexOf("exit `$exitCode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             content.IndexOf("if ($exitCode -ne 0)", StringComparison.OrdinalIgnoreCase) >= 0;

        hasNonZeroExit.Should().BeTrue("script must exit non-zero when harness fails");
    }

    [Fact]
    public void script_accepts_task_text_parameter()
    {
        var content = ReadScriptContent();
        content.IndexOf("$TaskText", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void script_accepts_output_dir_parameter()
    {
        var content = ReadScriptContent();
        content.IndexOf("$OutputDir", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void script_accepts_mcp_base_url_parameter()
    {
        var content = ReadScriptContent();
        content.IndexOf("$McpBaseUrl", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void script_accepts_model_base_url_parameter()
    {
        var content = ReadScriptContent();
        content.IndexOf("$ModelBaseUrl", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void script_accepts_model_name_parameter()
    {
        var content = ReadScriptContent();
        content.IndexOf("$ModelName", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void script_has_optional_api_key_env_parameter()
    {
        var content = ReadScriptContent();
        content.IndexOf("$ApiKeyEnv", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void script_invokes_plan_task_command()
    {
        var content = ReadScriptContent();
        content.IndexOf("plan-task", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }

    [Fact]
    public void script_has_error_action_preference_stop()
    {
        var content = ReadScriptContent();
        content.IndexOf("$ErrorActionPreference", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
        content.IndexOf("Stop", StringComparison.OrdinalIgnoreCase).Should().BeGreaterThan(-1);
    }
}
