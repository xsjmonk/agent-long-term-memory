using System.Text.Json;
using HarnessMcp.ControlPlane;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Tests that prove hard-stop behavior, flow bypass prevention, and canonical file presence.
/// These tests simulate what happens when an agent deviates from the harness loop and verify
/// that the harness enforces the flow strictly at every stage.
///
/// Also verifies that:
/// - Canonical rule files exist and stale files are absent
/// - Invalid artifacts hard-stop the flow
/// - The flow cannot continue after error without resubmitting a valid artifact
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class HardStopAndLoopTests : IDisposable
{
    private readonly string _testSessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _stateMachine;

    public HardStopAndLoopTests()
    {
        _testSessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-hardstop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testSessionsRoot);
        _store = new SessionStore(_testSessionsRoot);
        _stateMachine = new HarnessStateMachine(_store, new ValidationOptions());
    }

    // --- Canonical file presence / stale file tests ---

    [Fact]
    public void CanonicalRuleFiles_AllExist_NotMissing()
    {
        var harnessRoot = GetHarnessRootOrFail();
        var canonicalFiles = new[]
        {
            "00-harness-control-plane.mdc",
            "01-harness-failure.mdc",
            "02-harness-execution.mdc",
            "03-harness-mcp-tool-calling.mdc",
            "04-harness-skill-activation.mdc"
        };

        foreach (var fileName in canonicalFiles)
        {
            var path = Path.Combine(harnessRoot, ".cursor", "rules", fileName);
            File.Exists(path).Should().BeTrue($"canonical rule file '{fileName}' must exist");
        }
    }

    [Fact]
    public void StaleRuleFiles_DoNotExist()
    {
        var harnessRoot = GetHarnessRootOrFail();
        var staleFiles = new[]
        {
            "00-planning-mode-harness-control.mdc",
            "01-planning-mode-failure-handling.mdc",
            "02-execution-mode-worker-only.mdc",
            "03-planning-mode-mcp-stage.mdc"
        };

        foreach (var fileName in staleFiles)
        {
            var path = Path.Combine(harnessRoot, ".cursor", "rules", fileName);
            File.Exists(path).Should().BeFalse(
                $"stale rule file '{fileName}' must be removed — canonical names must be used exclusively");
        }
    }

    // --- Hard stop tests ---

    [Fact]
    public void HardStop_InvalidRequirementIntent_StopsFlow()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Task" }).SessionId;

        var invalid = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": """" }");
        var r = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = invalid }
        });

        r.Success.Should().BeFalse();
        r.Stage.Should().Be("error");
        r.NextAction.Should().Be(HarnessActionName.StopWithError);
        r.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void HardStop_AfterInvalidArtifact_FlowCannotContinue()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Task" }).SessionId;

        // Submit invalid artifact → session enters error state
        var invalid = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": """" }");
        _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = invalid }
        });

        // Attempt to continue with any other action — must be blocked
        var continueAttempt = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement("{}") }
        });

        continueAttempt.Success.Should().BeFalse("flow must not continue after error without resubmitting a valid artifact");
    }

    [Fact]
    public void HardStop_AfterError_AgentCanFixAndResubmit()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Task" }).SessionId;

        // Submit invalid
        var invalid = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": """" }");
        _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = invalid }
        });

        // After error, agent must fix and resubmit — but this requires starting a new session
        // because harness hard-stops on error (session is in error state)
        // The correct recovery path is cancel + new session
        var cancelResult = _stateMachine.CancelSession(sessionId);
        cancelResult.NextAction.Should().Be(HarnessActionName.StopWithError);

        // Start fresh session and submit valid artifact
        var newSession = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Task" });
        var valid = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""Add feature"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""low""
        }");

        var fixedResult = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = newSession.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = valid }
        });

        fixedResult.Success.Should().BeTrue("after cancellation and new session, valid artifact must be accepted");
        fixedResult.Stage.Should().Be("need_retrieval_chunk_set");
    }

    [Fact]
    public void HardStop_InvalidMcpResponseShape_StopsFlow()
    {
        var sessionId = StartAndReachMcpStage();

        // Submit wrong shape — uses 'results' not 'chunk_results'
        var wrongShape = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""results"": []
        }");

        var r = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = wrongShape }
        });

        r.Success.Should().BeFalse();
        r.Stage.Should().Be("error");
        r.Errors.Should().Contain(e => e.Contains("chunk_results"));
    }

    [Fact]
    public void HardStop_WrongActionForStage_StopsFlow()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Task" }).SessionId;

        // Session is at need_requirement_intent — submit wrong action
        var r = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = "wrong_action_name",
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement("{}") }
        });

        r.Success.Should().BeFalse();
        r.Stage.Should().Be("error");
        r.Errors.Should().Contain(e => e.Contains("Expected action"));
    }

    [Fact]
    public void HardStop_MissingSessionId_ReturnsError()
    {
        var r = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = "non-existent-session-id",
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement("{}") }
        });

        r.Success.Should().BeFalse();
        r.NextAction.Should().Be(HarnessActionName.StopWithError);
    }

    [Fact]
    public void HardStop_FlowCannotCompleteWithoutAllStages()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Task" }).SessionId;

        // Try to jump directly to complete without any stages
        var r = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.Complete,
            Artifact = new Artifact { ArtifactType = "Complete", Value = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement("{}") }
        });

        r.Success.Should().BeFalse();
        r.Stage.Should().Be("error");
    }

    [Fact]
    public void HardStop_InvalidChunkSet_MissingCoreTaskChunk()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Task" }).SessionId;
        SubmitValidRequirementIntent(sessionId);

        // Submit chunk set with no core_task chunk
        var noCorTask = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""complexity"": ""low"",
            ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""constraint"", ""text"": ""must not break API"" }
            ]
        }");

        var r = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = noCorTask }
        });

        r.Success.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Contains("core_task"));
    }

    [Fact]
    public void HardStop_ExecutionPlanMissingCanonicalFields_StopsFlow()
    {
        var validator = new Validators.ExecutionPlanValidator(new ValidationOptions());

        // Plan with old 'objective' field and missing 'task_id', 'task', 'forbidden_actions'
        var nonCanonical = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""objective"": ""Build something"",
            ""scope"": ""UI"",
            ""constraints"": [],
            ""steps"": [],
            ""deliverables"": []
        }");

        var result = validator.Validate(nonCanonical, null);
        result.IsValid.Should().BeFalse("non-canonical execution plan must fail validation");
        result.Errors.Should().Contain(e => e.Contains("task_id") || e.Contains("task"),
            "must require canonical 'task_id' and 'task' fields");
    }

    [Fact]
    public void HardStop_WorkerPacketMissingGoal_StopsFlow()
    {
        var validator = new Validators.WorkerExecutionPacketValidator();

        // Packet uses 'objective' instead of canonical 'goal'
        var nonCanonical = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""objective"": ""Build something"",
            ""hard_constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""execution_rules"": [""Do NOT retrieve long-term memory. Do NOT replan.""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""required_output_sections"": [""per_step_results""]
        }");

        var result = validator.Validate(nonCanonical, HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement("{}"));
        result.IsValid.Should().BeFalse("worker packet with 'objective' instead of 'goal' must fail validation");
        result.Errors.Should().Contain(e => e.Contains("goal"),
            "must require canonical 'goal' field");
    }

    // --- Helpers ---

    private string StartAndReachMcpStage()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Task" }).SessionId;
        SubmitValidRequirementIntent(sessionId);
        SubmitValidChunkSet(sessionId);
        SubmitValidChunkQualityReport(sessionId);
        return sessionId;
    }

    private void SubmitValidRequirementIntent(string sessionId)
    {
        var intent = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""Add feature"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""low""
        }");
        _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = intent }
        });
    }

    private void SubmitValidChunkSet(string sessionId)
    {
        var chunkSet = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""complexity"": ""low"",
            ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""Implement UI feature."" }
            ]
        }");
        _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = chunkSet }
        });
    }

    private void SubmitValidChunkQualityReport(string sessionId)
    {
        var report = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
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
        _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentValidateChunkQuality,
            Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = report }
        });
    }

    private static string GetHarnessRootOrFail()
    {
        var root = FindHarnessRoot();
        if (root == null)
            throw new DirectoryNotFoundException(
                "Could not locate harness repository root.");
        return root;
    }

    private static string? FindHarnessRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "tests", "HarnessMcp.ControlPlane.Tests")))
                return current;
            if (Directory.Exists(Path.Combine(current, "src", "HarnessMcp.ControlPlane")))
                return current;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return null;
    }

    public void Dispose()
    {
        if (Directory.Exists(_testSessionsRoot))
            Directory.Delete(_testSessionsRoot, true);
    }
}
