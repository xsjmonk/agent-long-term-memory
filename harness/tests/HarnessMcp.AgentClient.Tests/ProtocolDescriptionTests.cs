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

        var forbidden = doc.RootElement.GetProperty("forbiddenAgentBehavior").EnumerateArray().Select(x => x.GetString()).ToArray();
        forbidden.Should().Contain("do not skip harness planning");
        forbidden.Should().Contain("do not retrieve long-term memory independently during execution");
        forbidden.Should().Contain("do not generate a replacement plan outside the harness");
    }
}

