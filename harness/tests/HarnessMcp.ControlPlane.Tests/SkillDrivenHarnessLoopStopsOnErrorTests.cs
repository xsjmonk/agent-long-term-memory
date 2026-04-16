using System.IO;
using System.Text.Json;
using FluentAssertions;
using HarnessMcp.ControlPlane;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Proves that the skill-driven harness loop hard-stops on any error and the session
/// is permanently locked in the error state until explicitly restarted.
///
/// Two-layer proof:
///   1. Skill-content: failure skill (01-harness-failure.mdc) mandates HARD STOP, STOP IMMEDIATELY,
///      prohibits repair by guessing, prohibits free-form fallback.
///   2. Harness behavior: submitting invalid artifacts or wrong actions sets session.stage = "error"
///      and all subsequent calls to that session return error — the harness mirrors the skill mandate.
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class SkillDrivenHarnessLoopStopsOnErrorTests : IDisposable
{
    private readonly string _sessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _sm;
    private const string FailureSkillFile = "01-harness-failure.mdc";

    public SkillDrivenHarnessLoopStopsOnErrorTests()
    {
        _sessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-stop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sessionsRoot);
        _store = new SessionStore(_sessionsRoot);
        _sm = new HarnessStateMachine(_store, new ValidationOptions());
    }

    // ==========================================
    // Layer 1: Failure skill mandates hard-stop behavior
    // ==========================================

    [Fact]
    public void FailureSkill_ContainsHardStop_Mandate()
    {
        var content = ReadSkillOrFail();
        content.Should().Contain("HARD STOP",
            "failure skill must mandate a HARD STOP on any harness error — soft guidance is insufficient");
    }

    [Fact]
    public void FailureSkill_ContainsStopImmediately_Mandate()
    {
        var content = ReadSkillOrFail();
        content.Should().Contain("STOP IMMEDIATELY",
            "failure skill must say STOP IMMEDIATELY — agents must not continue after a harness error");
    }

    [Fact]
    public void FailureSkill_ProhibitsRepairByGuessing()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("repair by guessing",
            "failure skill must explicitly prohibit 'repair by guessing' — agents must surface errors verbatim");
    }

    [Fact]
    public void FailureSkill_ProhibitsFreeFormFallback()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("free-form",
            "failure skill must prohibit free-form planning fallback on harness error");
        content.Should().Contain("NEVER",
            "failure skill must use NEVER to make prohibitions unambiguous");
    }

    [Fact]
    public void FailureSkill_ContainsHardStopChecklist()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("hard-stop checklist",
            "failure skill must contain a hard-stop checklist for agents to follow");
    }

    [Fact]
    public void FailureSkill_DistinguishesAllFourFailureTypes()
    {
        var content = ReadSkillOrFail();
        content.Should().Contain("Harness Validation Failure");
        content.Should().Contain("MCP Tool Call Failure");
        content.Should().Contain("Wrapper");
        content.ToLowerInvariant().Should().Contain("mismatch",
            "failure skill must cover Session Resume/State Mismatch as the 4th failure type");
    }

    [Fact]
    public void FailureSkill_ContainsSessionResumeMismatch_Guidance()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("get-session-status",
            "failure skill must instruct agents to call get-session-status when session state is uncertain");
        content.ToLowerInvariant().Should().Contain("get-next-step",
            "failure skill must reference get-next-step for re-syncing after mismatch");
    }

    // ==========================================
    // Layer 2: Harness enforces hard-stop — session locked in error state
    // ==========================================

    [Fact]
    public void Harness_WrongAction_SetsSessionToErrorState()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        r0.Stage.Should().Be("need_requirement_intent");

        // Submit wrong action at stage 1
        var errResp = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet, // wrong — expected RequirementIntent
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = HarnessJson.ParseJsonElement("{}") }
        });

        errResp.Success.Should().BeFalse("wrong action must hard-stop");
        errResp.Stage.Should().Be("error");
        errResp.NextAction.Should().Be(HarnessActionName.StopWithError);
        errResp.Errors.Should().NotBeEmpty("error state must include error messages");
    }

    [Fact]
    public void Harness_SessionStaysInError_AfterHardStop_AllSubsequentCallsReturnError()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });

        // Trigger error by submitting wrong action
        _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = HarnessJson.ParseJsonElement("{}") }
        });

        // All subsequent attempts must also fail — session is permanently in error
        var retry1 = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = HarnessJson.ParseJsonElement(@"{ ""task_id"": ""t1"", ""task_type"": ""ui"", ""goal"": ""g"", ""hard_constraints"": [], ""risk_signals"": [], ""complexity"": ""low"" }") }
        });

        retry1.Success.Should().BeFalse("session in error state must reject all subsequent submissions");
        retry1.Stage.Should().Be("error");
        retry1.NextAction.Should().Be(HarnessActionName.StopWithError);
    }

    [Fact]
    public void Harness_MalformedArtifact_SetsSessionToErrorState()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });

        // Submit completely empty artifact — should fail validation
        var errResp = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = HarnessJson.ParseJsonElement("{}") }
        });

        errResp.Success.Should().BeFalse("empty RequirementIntent must fail validation and hard-stop");
        errResp.Stage.Should().Be("error");
        errResp.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Harness_GetNextStep_OnErrorSession_ReturnsStopWithError()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });

        // Trigger error
        _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = HarnessJson.ParseJsonElement("{}") }
        });

        // GetNextStep on error session must return stop-with-error
        var status = _sm.GetNextStep(r0.SessionId);
        status.NextAction.Should().Be(HarnessActionName.StopWithError,
            "GetNextStep on error session mirrors the STOP IMMEDIATELY mandate in the failure skill");
        status.Stage.Should().Be("error");
    }

    [Fact]
    public void Harness_GetSessionStatus_OnErrorSession_ReturnsErrorStage()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });

        // Trigger error
        _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = HarnessJson.ParseJsonElement("{}") }
        });

        // get-session-status (as mandated by failure skill for mismatch recovery) returns error stage
        var status = _sm.GetSessionStatus(r0.SessionId);
        status.Stage.Should().Be("error",
            "get-session-status must surface the error stage so the agent can follow the failure skill's mismatch guidance");
        status.Errors.Should().NotBeEmpty("error messages must be preserved in session status");
    }

    [Fact]
    public void Harness_MultipleHardStopScenarios_AllLockSessionInError()
    {
        // Test three different hard-stop triggers to ensure all paths lock the session
        var scenarios = new[]
        {
            ("wrong-action", HarnessActionName.AgentGenerateRetrievalChunkSet, "RetrievalChunkSet", "{}"),
            ("wrong-action-mcp", HarnessActionName.AgentCallMcpRetrieveMemoryByChunks, "RetrieveMemoryByChunksResponse", "{}")
        };

        foreach (var (label, wrongAction, wrongArtifactType, wrongPayload) in scenarios)
        {
            var r0 = _sm.StartSession(new StartSessionRequest { RawTask = $"Design something ({label})" });

            var errResp = _sm.SubmitStepResult(new SubmitStepResultRequest
            {
                SessionId = r0.SessionId,
                CompletedAction = wrongAction,
                Artifact = new Artifact { ArtifactType = wrongArtifactType, Value = HarnessJson.ParseJsonElement(wrongPayload) }
            });

            errResp.Success.Should().BeFalse($"scenario '{label}' must hard-stop");
            errResp.Stage.Should().Be("error", $"scenario '{label}' must lock session in error state");
        }
    }

    // --- Helpers ---

    private string ReadSkillOrFail()
    {
        var root = FindHarnessRoot() ?? throw new DirectoryNotFoundException("Could not locate harness root.");
        var path = Path.Combine(root, ".cursor", "rules", FailureSkillFile);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Failure skill not found at: {path}");
        return File.ReadAllText(path);
    }

    private static string? FindHarnessRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "tests", "HarnessMcp.ControlPlane.Tests"))) return current;
            if (Directory.Exists(Path.Combine(current, "src", "HarnessMcp.ControlPlane"))) return current;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return null;
    }

    public void Dispose()
    {
        if (Directory.Exists(_sessionsRoot)) Directory.Delete(_sessionsRoot, true);
    }
}
