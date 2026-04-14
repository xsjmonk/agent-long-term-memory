using System.Text.Json;
using System.Linq;
using FluentAssertions;
using HarnessMcp.AgentClient.Cli;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class ProtocolDescriptionTests
{
    [Fact]
    public void describe_protocol_returns_expected_protocol_name_and_primary_command()
    {
        var json = DescribeProtocolCommand.GetProtocolJson();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("protocolName").GetString().Should().Be("HarnessMcp.AgentClient.PlanTaskProtocol");
        doc.RootElement.GetProperty("primaryCommand").GetString().Should().Be("plan-task");
        doc.RootElement.GetProperty("protocolVersion").GetString().Should().Be("1.0");
    }

    [Fact]
    public void describe_protocol_includes_required_agent_and_forbidden_agent_behavior()
    {
        var json = DescribeProtocolCommand.GetProtocolJson();
        using var doc = JsonDocument.Parse(json);

        var required = doc.RootElement.GetProperty("requiredAgentBehavior").EnumerateArray().Select(x => x.GetString()).ToArray();
        required.Should().Contain("call plan-task before execution work begins");
        required.Should().Contain("use the machine-readable result manifest instead of guessing artifact names");
        required.Should().Contain("use the worker packet produced by the harness as the execution handoff");
        required.Should().Contain("follow nextAction exactly as specified in the manifest");
        required.Should().Contain("stop and fix errors when harness returns success=false");

        var forbidden = doc.RootElement.GetProperty("forbiddenAgentBehavior").EnumerateArray().Select(x => x.GetString()).ToArray();
        forbidden.Should().Contain("do not skip harness planning for non-trivial tasks");
        forbidden.Should().Contain("do not begin coding or direct planning before harness success");
        forbidden.Should().Contain("do not retrieve long-term memory independently outside the harness flow");
        forbidden.Should().Contain("do not generate a replacement plan outside the harness");
        forbidden.Should().Contain("do not expand scope beyond the worker packet steps");
        forbidden.Should().Contain("do not reinterpret the task at the architecture level during execution");
    }

    [Fact]
    public void describe_protocol_requires_harness_invocation_before_execution()
    {
        var json = DescribeProtocolCommand.GetProtocolJson();
        using var doc = JsonDocument.Parse(json);

        var required = doc.RootElement.GetProperty("requiredAgentBehavior").EnumerateArray()
            .Select(x => x.GetString() ?? "").ToArray();

        var hasPreExecutionRequirement = required.Any(r =>
            r.Contains("before", StringComparison.OrdinalIgnoreCase) &&
            (r.Contains("execution", StringComparison.OrdinalIgnoreCase) || r.Contains("begin", StringComparison.OrdinalIgnoreCase)));

        hasPreExecutionRequirement.Should().BeTrue(
            "protocol must state that harness must be invoked before execution work begins");
    }

    [Fact]
    public void describe_protocol_describes_authoritative_manifest_usage()
    {
        var json = DescribeProtocolCommand.GetProtocolJson();
        using var doc = JsonDocument.Parse(json);

        var required = doc.RootElement.GetProperty("requiredAgentBehavior").EnumerateArray()
            .Select(x => x.GetString() ?? "").ToArray();

        var mentionsManifest = required.Any(r =>
            r.Contains("manifest", StringComparison.OrdinalIgnoreCase) &&
            r.Contains("result", StringComparison.OrdinalIgnoreCase));

        mentionsManifest.Should().BeTrue(
            "protocol must describe machine-readable manifest as authoritative result contract");
    }

    [Fact]
    public void describe_protocol_describes_worker_packet_handoff()
    {
        var json = DescribeProtocolCommand.GetProtocolJson();
        using var doc = JsonDocument.Parse(json);

        var required = doc.RootElement.GetProperty("requiredAgentBehavior").EnumerateArray()
            .Select(x => x.GetString() ?? "").ToArray();

        var mentionsHandoff = required.Any(r =>
            r.Contains("worker", StringComparison.OrdinalIgnoreCase) &&
            (r.Contains("handoff", StringComparison.OrdinalIgnoreCase) || r.Contains("execution", StringComparison.OrdinalIgnoreCase)));

        mentionsHandoff.Should().BeTrue(
            "protocol must describe worker packet as authoritative execution handoff");
    }

    [Fact]
    public void describe_protocol_explicitly_forbids_worker_memory_retrieval()
    {
        var json = DescribeProtocolCommand.GetProtocolJson();
        using var doc = JsonDocument.Parse(json);

        var forbidden = doc.RootElement.GetProperty("forbiddenAgentBehavior").EnumerateArray()
            .Select(x => x.GetString() ?? "").ToArray();

        var forbidsMemoryRetrieval = forbidden.Any(f =>
            f.Contains("memory", StringComparison.OrdinalIgnoreCase) &&
            (f.Contains("independent", StringComparison.OrdinalIgnoreCase) || f.Contains("retrieve", StringComparison.OrdinalIgnoreCase)));

        forbidsMemoryRetrieval.Should().BeTrue(
            "protocol must explicitly forbid worker-side independent memory retrieval");
    }

    [Fact]
    public void describe_protocol_specifies_success_next_action()
    {
        var json = DescribeProtocolCommand.GetProtocolJson();
        using var doc = JsonDocument.Parse(json);

        var nextActionOnSuccess = doc.RootElement.GetProperty("nextActionOnSuccess").GetString();
        nextActionOnSuccess.Should().Be("paste_worker_packet_into_execution_agent");
    }

    [Fact]
    public void describe_protocol_has_failure_exit_meaning()
    {
        var json = DescribeProtocolCommand.GetProtocolJson();
        using var doc = JsonDocument.Parse(json);

        var failureMeaning = doc.RootElement.GetProperty("failureExitMeaning").GetString();
        failureMeaning.Should().NotBeNullOrEmpty();
        failureMeaning.Should().Contain("false");
    }

    [Fact]
    public void describe_protocol_has_success_exit_meaning()
    {
        var json = DescribeProtocolCommand.GetProtocolJson();
        using var doc = JsonDocument.Parse(json);

        var successMeaning = doc.RootElement.GetProperty("successExitMeaning").GetString();
        successMeaning.Should().NotBeNullOrEmpty();
        successMeaning.Should().Contain("true");
    }
}

