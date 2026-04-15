using System.Text.Json;
using HarnessMcp.ControlPlane;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Tests that verify the skill files drive the harness loop correctly.
/// These tests simulate a generic agent following the skills and verify
/// that harness and skills together control the flow.
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class SkillDrivenFlowTests : IDisposable
{
    private readonly string _testSessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _stateMachine;

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
        throw new FileNotFoundException(
            $"Required canonical rule file '{ruleName}' not found. " +
            "Implementation is not complete until all canonical rule files exist.");
    }

    // --- Skill content tests ---

    [Fact]
    public void PlanningSkill_UsesHarnessAsSingleEntryPoint()
    {
        var ruleContent = File.ReadAllText(GetRulePath("00-harness-control-plane.mdc"));
        ruleContent.Should().Contain("invoke-harness-control-plane.ps1");
        ruleContent.Should().Contain("start-session");
        ruleContent.Should().NotContain("HarnessMcp.AgentClient");
    }

    [Fact]
    public void PlanningSkill_RequiresSubmitAfterEveryStage()
    {
        var ruleContent = File.ReadAllText(GetRulePath("00-harness-control-plane.mdc"));
        ruleContent.Should().Contain("submit-step-result");
        ruleContent.Should().Contain("nextAction");
    }

    [Fact]
    public void McpSkill_RequiresExactToolCall()
    {
        var ruleContent = File.ReadAllText(GetRulePath("03-harness-mcp-tool-calling.mdc"));
        ruleContent.Should().Contain("nextAction");
        ruleContent.Should().Contain("EXACTLY");
        ruleContent.Should().Contain("retrieve_memory_by_chunks");
        ruleContent.Should().Contain("merge_retrieval_results");
        ruleContent.Should().Contain("build_memory_context_pack");
    }

    [Fact]
    public void FailureSkill_StopsOnHarnessError()
    {
        var ruleContent = File.ReadAllText(GetRulePath("01-harness-failure.mdc"));
        ruleContent.Should().Contain("stop_with_error");
        ruleContent.Should().Contain("STOP");
        ruleContent.Should().Contain("STOP IMMEDIATELY");
    }

    [Fact]
    public void ExecutionSkill_ForbidsIndependentMemoryRetrieval()
    {
        var ruleContent = File.ReadAllText(GetRulePath("02-harness-execution.mdc"));
        ruleContent.Should().Contain("long-term memory");
        ruleContent.Should().Contain("forbidden");
        ruleContent.Should().Contain("DO NOT Retrieve");
    }

    [Fact]
    public void ActivationSkill_IsSemanticNotLexical()
    {
        var ruleContent = File.ReadAllText(GetRulePath("04-harness-skill-activation.mdc"));
        ruleContent.Should().Contain("semantic");
        ruleContent.Should().Contain("planning intent");
    }

    [Fact]
    public void ActivationSkill_DescribesActivationScenarios()
    {
        var ruleContent = File.ReadAllText(GetRulePath("04-harness-skill-activation.mdc"));
        ruleContent.Should().Contain("design");
        ruleContent.Should().Contain("approach");
        ruleContent.Should().Contain("activate");
    }

    [Fact]
    public void ActivationSkill_DescribesNonActivationScenarios()
    {
        var ruleContent = File.ReadAllText(GetRulePath("04-harness-skill-activation.mdc"));
        ruleContent.Should().Contain("trivial");
        ruleContent.Should().Contain("casual");
    }

    [Fact]
    public void ActivationSkill_LinksToHarnessControlPlane()
    {
        var ruleContent = File.ReadAllText(GetRulePath("04-harness-skill-activation.mdc"));
        ruleContent.Should().Contain("00-harness-control-plane");
    }

    // --- Harness state-machine behavior tests ---

    [Fact]
    public void Harness_StartSession_ReturnsRequirementIntentAction()
    {
        var r = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Add feature" });
        r.Success.Should().BeTrue();
        r.NextAction.Should().Be(HarnessActionName.AgentGenerateRequirementIntent);
        r.Stage.Should().Be("need_requirement_intent");
    }

    [Fact]
    public void Harness_AfterStart_AgentMustSubmitRequirementIntent_BeforeAnythingElse()
    {
        var r = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Add feature" });
        var sessionId = r.SessionId;

        // Simulate agent trying to skip directly to chunk set
        var wrongResult = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = JsonSerializer.Deserialize<JsonElement>("{}") }
        });

        wrongResult.Success.Should().BeFalse();
        wrongResult.Stage.Should().Be("error");
        wrongResult.Errors.Should().Contain(e => e.Contains("Expected action"));
    }

    [Fact]
    public void Harness_ProvidesMcpToolName_AtMcpStage()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Add feature" }).SessionId;
        SubmitRequirementIntent(sessionId);
        SubmitRetrievalChunkSet(sessionId);
        var r = SubmitChunkQualityReport(sessionId);

        r.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");
        r.ToolName.Should().Be("retrieve_memory_by_chunks");
        r.Payload.Should().ContainKey("request");
    }

    [Fact]
    public void Harness_InvalidArtifact_HardStops_CannotContinueWithoutFix()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Add feature" }).SessionId;

        // Submit invalid RequirementIntent
        var invalid = JsonSerializer.Deserialize<JsonElement>(@"{ ""task_id"": """" }");
        var r1 = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = invalid }
        });

        r1.Success.Should().BeFalse();
        r1.Stage.Should().Be("error");
        r1.NextAction.Should().Be(HarnessActionName.StopWithError);

        // Agent cannot continue — next call also fails
        var r2 = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = JsonSerializer.Deserialize<JsonElement>("{}") }
        });

        r2.Success.Should().BeFalse();
    }

    // --- Helpers ---

    private StepResponse SubmitRequirementIntent(string sessionId, string complexity = "low", string[]? hardConstraints = null, string[]? riskSignals = null)
    {
        var intent = JsonSerializer.Deserialize<JsonElement>($@"
        {{
            ""task_id"": ""task-1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""implement new feature"",
            ""hard_constraints"": [{string.Join(",", (hardConstraints ?? Array.Empty<string>()).Select(c => $"\"{c}\""))}],
            ""risk_signals"": [{string.Join(",", (riskSignals ?? Array.Empty<string>()).Select(r => $"\"{r}\""))}],
            ""complexity"": ""{complexity}""
        }}");

        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = intent }
        });
    }

    private StepResponse SubmitRetrievalChunkSet(string sessionId)
    {
        var chunkSet = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""task_id"": ""task-1"",
            ""complexity"": ""low"",
            ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""Implement new feature in the UI layer."" }
            ]
        }");

        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = chunkSet }
        });
    }

    private StepResponse SubmitChunkQualityReport(string sessionId)
    {
        var report = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""isValid"": true,
            ""has_core_task"": true,
            ""has_constraint"": false,
            ""has_risk"": false,
            ""has_pattern"": false,
            ""has_similar_case"": false,
            ""errors"": [],
            ""warnings"": []
        }");

        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentValidateChunkQuality,
            Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = report }
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_testSessionsRoot))
            Directory.Delete(_testSessionsRoot, true);
    }
}
