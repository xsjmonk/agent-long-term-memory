using System.Text.Json;
using HarnessMcp.ControlPlane;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

public class SkillDrivenFlowTests : IDisposable
{
    private readonly string _testSessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _stateMachine;
    private static readonly string RepoRoot = Directory.GetCurrentDirectory();

    public SkillDrivenFlowTests()
    {
        _testSessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-skill-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testSessionsRoot);
        _store = new SessionStore(_testSessionsRoot);
        _stateMachine = new HarnessStateMachine(_store, new ValidationOptions());
    }

    private static string GetRulePath(string ruleName)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, ".cursor", "rules", ruleName);
            if (File.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        throw new FileNotFoundException($"Could not find {ruleName}");
    }

    [Fact]
    public void PlanningSkill_UsesHarnessAsSingleEntryPoint()
    {
        var rulePath = GetRulePath("00-harness-control-plane.mdc");
        var ruleContent = File.ReadAllText(rulePath);
        ruleContent.Should().Contain("invoke-harness-control-plane.ps1");
        ruleContent.Should().Contain("start-session");
        ruleContent.Should().NotContain("HarnessMcp.AgentClient");
    }

    [Fact]
    public void PlanningSkill_RequiresSubmitAfterEveryStage()
    {
        var rulePath = GetRulePath("00-harness-control-plane.mdc");
        var ruleContent = File.ReadAllText(rulePath);
        ruleContent.Should().Contain("submit-step-result");
        ruleContent.Should().Contain("nextAction");
    }

    [Fact]
    public void McpSkill_RequiresExactToolCall()
    {
        var rulePath = GetRulePath("03-harness-mcp-tool-calling.mdc");
        var ruleContent = File.ReadAllText(rulePath);
        ruleContent.Should().Contain("nextAction");
        ruleContent.Should().Contain("EXACTLY");
    }

    [Fact]
    public void FailureSkill_StopsOnHarnessError()
    {
        var rulePath = GetRulePath("01-harness-failure.mdc");
        var ruleContent = File.ReadAllText(rulePath);
        ruleContent.Should().Contain("stop_with_error");
        ruleContent.Should().Contain("STOP");
    }

    [Fact]
    public void ExecutionSkill_ForbidsIndependentMemoryRetrieval()
    {
        var rulePath = GetRulePath("02-harness-execution.mdc");
        var ruleContent = File.ReadAllText(rulePath);
        ruleContent.Should().Contain("long-term memory");
        ruleContent.Should().Contain("forbidden");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testSessionsRoot))
            Directory.Delete(_testSessionsRoot, true);
    }
}