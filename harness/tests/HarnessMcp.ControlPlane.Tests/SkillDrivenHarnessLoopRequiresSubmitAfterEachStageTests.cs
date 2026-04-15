using System.IO;
using System.Text.Json;
using FluentAssertions;
using HarnessMcp.ControlPlane;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Proves that the skill-driven harness loop requires exactly one submit per stage —
/// agents cannot skip stages, batch stages, or re-submit the same stage.
///
/// Two-layer proof:
///   1. Skill-content: planning skill (00-harness-control-plane.mdc) contains a do-not-skip/
///      do-not-batch/do-not-bypass section mandating one-at-a-time stage progression.
///   2. Harness behavior: submitting the wrong action for the current stage hard-stops the session.
///      After a valid submit, the stage advances exactly once. Re-submitting the same action
///      (now the wrong action for the new stage) is rejected.
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class SkillDrivenHarnessLoopRequiresSubmitAfterEachStageTests : IDisposable
{
    private readonly string _sessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _sm;
    private const string PlanningSkillFile = "00-harness-control-plane.mdc";

    public SkillDrivenHarnessLoopRequiresSubmitAfterEachStageTests()
    {
        _sessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-submit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sessionsRoot);
        _store = new SessionStore(_sessionsRoot);
        _sm = new HarnessStateMachine(_store, new ValidationOptions());
    }

    // ==========================================
    // Layer 1: Planning skill mandates one-stage-at-a-time progression
    // ==========================================

    [Fact]
    public void PlanningSkill_Contains_DoNotSkip_DoNotBatch_DoNotBypass_Section()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("do-not-skip",
            "planning skill must contain a do-not-skip section — agents must submit after every stage");
    }

    [Fact]
    public void PlanningSkill_Contains_MustWording_ForStageSequence()
    {
        var content = ReadSkillOrFail();
        content.Should().Contain("MUST",
            "planning skill must use MUST to enforce the stage sequence");
    }

    [Fact]
    public void PlanningSkill_Contains_NeverWording_ForSkippingStages()
    {
        var content = ReadSkillOrFail();
        content.Should().Contain("NEVER",
            "planning skill must use NEVER to prohibit stage-skipping");
    }

    [Fact]
    public void PlanningSkill_Contains_ResumeInstructions()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("how to resume",
            "planning skill must include how-to-resume instructions for when a session is interrupted");
        content.Should().Contain("get-next-step",
            "resume section must reference get-next-step so the agent knows what action to submit next");
    }

    [Fact]
    public void PlanningSkill_ContainsStageTable_WithAllStages()
    {
        var content = ReadSkillOrFail();
        content.Should().Contain("need_requirement_intent");
        content.Should().Contain("need_execution_plan");
        content.Should().Contain("need_worker_execution_packet");
        content.Should().Contain("| `need_requirement_intent`",
            "stage table must use markdown table format");
    }

    // ==========================================
    // Layer 2: Harness enforces one-submit-per-stage
    // ==========================================

    [Fact]
    public void Harness_CannotSkipStage1_BySubmittingStage2Action()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        r0.Stage.Should().Be("need_requirement_intent");

        // Try to skip stage 1 (RequirementIntent) and submit stage 2 (RetrievalChunkSet)
        var skip = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = JsonSerializer.Deserialize<JsonElement>("{}") }
        });

        skip.Success.Should().BeFalse("cannot skip stage 1 — harness requires RequirementIntent before RetrievalChunkSet");
        skip.Stage.Should().Be("error");
    }

    [Fact]
    public void Harness_CannotSkipMcpStages_BySubmittingExecutionPlan_Immediately()
    {
        // After RequirementIntent + ChunkSet + ChunkValidation, session is at need_mcp_retrieve_memory_by_chunks
        // Trying to jump to ExecutionPlan must fail
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        SubmitRequirementIntent(r0.SessionId);
        SubmitRetrievalChunkSet(r0.SessionId);
        var r3 = SubmitChunkQualityReport(r0.SessionId);
        r3.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");

        var skip = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateExecutionPlan,
            Artifact = new Artifact { ArtifactType = "ExecutionPlan", Value = JsonSerializer.Deserialize<JsonElement>("{}") }
        });

        skip.Success.Should().BeFalse("cannot skip MCP stages — harness requires all 3 MCP stages before ExecutionPlan");
        skip.Stage.Should().Be("error");
    }

    [Fact]
    public void Harness_CannotSkipExecutionPlan_BySubmittingWorkerPacket_Immediately()
    {
        // After all MCP stages, session is at need_execution_plan
        // Trying to submit WorkerExecutionPacket before ExecutionPlan must fail
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        SubmitRequirementIntent(r0.SessionId);
        SubmitRetrievalChunkSet(r0.SessionId);
        SubmitChunkQualityReport(r0.SessionId);
        SubmitRetrieveMemoryByChunksResponse(r0.SessionId);
        SubmitMergeRetrievalResultsResponse(r0.SessionId);
        var r6 = SubmitBuildMemoryContextPackResponse(r0.SessionId);
        r6.Stage.Should().Be("need_execution_plan");

        var skip = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateWorkerExecutionPacket,
            Artifact = new Artifact { ArtifactType = "WorkerExecutionPacket", Value = JsonSerializer.Deserialize<JsonElement>("{}") }
        });

        skip.Success.Should().BeFalse("cannot skip ExecutionPlan — harness requires ExecutionPlan before WorkerExecutionPacket");
        skip.Stage.Should().Be("error");
    }

    [Fact]
    public void Harness_AfterValidSubmit_StageAdvancesExactlyOnce()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        r0.Stage.Should().Be("need_requirement_intent");

        var r1 = SubmitRequirementIntent(r0.SessionId);
        r1.Success.Should().BeTrue();
        r1.Stage.Should().Be("need_retrieval_chunk_set",
            "after valid RequirementIntent submit, stage advances exactly once to need_retrieval_chunk_set");

        var r2 = SubmitRetrievalChunkSet(r0.SessionId);
        r2.Success.Should().BeTrue();
        r2.Stage.Should().Be("need_retrieval_chunk_validation",
            "after valid RetrievalChunkSet submit, stage advances exactly once");
    }

    [Fact]
    public void Harness_CannotResubmitSameStage_AfterAdvancing()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        var r1 = SubmitRequirementIntent(r0.SessionId);
        r1.Stage.Should().Be("need_retrieval_chunk_set");

        // Try to re-submit RequirementIntent (now wrong for the current stage)
        var resubmit = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact
            {
                ArtifactType = "RequirementIntent",
                Value = JsonSerializer.Deserialize<JsonElement>(@"{ ""task_id"": ""task-1"", ""task_type"": ""ui"", ""goal"": ""g"", ""hard_constraints"": [], ""risk_signals"": [], ""complexity"": ""low"" }")
            }
        });

        resubmit.Success.Should().BeFalse("cannot re-submit RequirementIntent after stage has advanced — harness requires the next action");
        resubmit.Stage.Should().Be("error");
    }

    [Fact]
    public void Harness_GetNextStep_AlwaysReturnsCurrentStageAction()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });

        // Before any submit: get-next-step returns stage 1 action
        var step1 = _sm.GetNextStep(r0.SessionId);
        step1.NextAction.Should().Be(HarnessActionName.AgentGenerateRequirementIntent);

        SubmitRequirementIntent(r0.SessionId);

        // After stage 1 submit: get-next-step returns stage 2 action
        var step2 = _sm.GetNextStep(r0.SessionId);
        step2.NextAction.Should().Be(HarnessActionName.AgentGenerateRetrievalChunkSet);
    }

    [Fact]
    public void Harness_EachStage_RequiresCorrectAction_CannotBeBatched()
    {
        // Verify that all 8 stages each require their specific action in sequence
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });

        // Stage 1
        var s1 = _sm.GetNextStep(r0.SessionId);
        s1.NextAction.Should().Be(HarnessActionName.AgentGenerateRequirementIntent);
        SubmitRequirementIntent(r0.SessionId);

        // Stage 2
        var s2 = _sm.GetNextStep(r0.SessionId);
        s2.NextAction.Should().Be(HarnessActionName.AgentGenerateRetrievalChunkSet);
        SubmitRetrievalChunkSet(r0.SessionId);

        // Stage 3
        var s3 = _sm.GetNextStep(r0.SessionId);
        s3.NextAction.Should().Be(HarnessActionName.AgentValidateChunkQuality);
        SubmitChunkQualityReport(r0.SessionId);

        // Stage 4 (MCP 1)
        var s4 = _sm.GetNextStep(r0.SessionId);
        s4.NextAction.Should().Be(HarnessActionName.AgentCallMcpRetrieveMemoryByChunks);
        SubmitRetrieveMemoryByChunksResponse(r0.SessionId);

        // Stage 5 (MCP 2)
        var s5 = _sm.GetNextStep(r0.SessionId);
        s5.NextAction.Should().Be(HarnessActionName.AgentCallMcpMergeRetrievalResults);
        SubmitMergeRetrievalResultsResponse(r0.SessionId);

        // Stage 6 (MCP 3)
        var s6 = _sm.GetNextStep(r0.SessionId);
        s6.NextAction.Should().Be(HarnessActionName.AgentCallMcpBuildMemoryContextPack);
        SubmitBuildMemoryContextPackResponse(r0.SessionId);

        // Stage 7
        var s7 = _sm.GetNextStep(r0.SessionId);
        s7.NextAction.Should().Be(HarnessActionName.AgentGenerateExecutionPlan);
        SubmitExecutionPlan(r0.SessionId);

        // Stage 8
        var s8 = _sm.GetNextStep(r0.SessionId);
        s8.NextAction.Should().Be(HarnessActionName.AgentGenerateWorkerExecutionPacket);
        var final = SubmitWorkerExecutionPacket(r0.SessionId);

        // Complete
        final.Stage.Should().Be("complete");
        final.NextAction.Should().Be(HarnessActionName.Complete);
    }

    // --- Helpers ---

    private StepResponse SubmitRequirementIntent(string sessionId)
    {
        var v = JsonSerializer.Deserialize<JsonElement>(@"{ ""task_id"": ""task-1"", ""task_type"": ""ui-change"", ""goal"": ""implement feature"", ""hard_constraints"": [], ""risk_signals"": [], ""complexity"": ""low"" }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentGenerateRequirementIntent, Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = v } });
    }

    private StepResponse SubmitRetrievalChunkSet(string sessionId)
    {
        var v = JsonSerializer.Deserialize<JsonElement>(@"{ ""task_id"": ""task-1"", ""complexity"": ""low"", ""chunks"": [{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""implement"" }] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet, Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = v } });
    }

    private StepResponse SubmitChunkQualityReport(string sessionId)
    {
        var v = JsonSerializer.Deserialize<JsonElement>(@"{ ""isValid"": true, ""has_core_task"": true, ""has_constraint"": false, ""has_risk"": false, ""has_pattern"": false, ""has_similar_case"": false, ""errors"": [], ""warnings"": [] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentValidateChunkQuality, Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = v } });
    }

    private StepResponse SubmitRetrieveMemoryByChunksResponse(string sessionId)
    {
        var v = JsonSerializer.Deserialize<JsonElement>(@"{ ""task_id"": ""task-1"", ""chunk_results"": [{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""results"": { ""decisions"": [], ""best_practices"": [{ ""knowledge_item_id"": ""k1"", ""title"": ""t"", ""summary"": ""s"" }], ""anti_patterns"": [], ""similar_cases"": [], ""constraints"": [], ""references"": [], ""structures"": [] } }] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks, Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = v } });
    }

    private StepResponse SubmitMergeRetrievalResultsResponse(string sessionId)
    {
        var v = JsonSerializer.Deserialize<JsonElement>(@"{ ""task_id"": ""task-1"", ""merged"": { ""decisions"": [], ""constraints"": [], ""best_practices"": [{ ""item"": { ""knowledge_item_id"": ""k1"", ""title"": ""t"", ""summary"": ""s"" }, ""supported_by_chunk_ids"": [""c1""], ""supported_by_chunk_types"": [""core_task""], ""merge_rationales"": [""relevant""] }], ""anti_patterns"": [], ""similar_cases"": [], ""references"": [], ""structures"": [] } }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentCallMcpMergeRetrievalResults, Artifact = new Artifact { ArtifactType = "MergeRetrievalResultsResponse", Value = v } });
    }

    private StepResponse SubmitBuildMemoryContextPackResponse(string sessionId)
    {
        var v = JsonSerializer.Deserialize<JsonElement>(@"{ ""task_id"": ""task-1"", ""memory_context_pack"": { ""must_follow"": [], ""best_practices"": [], ""avoid"": [], ""similar_case_guidance"": [], ""retrieval_support"": { ""multi_supported_items"": [], ""single_route_important_items"": [] } } }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentCallMcpBuildMemoryContextPack, Artifact = new Artifact { ArtifactType = "BuildMemoryContextPackResponse", Value = v } });
    }

    private StepResponse SubmitExecutionPlan(string sessionId)
    {
        var v = JsonSerializer.Deserialize<JsonElement>(@"{ ""task_id"": ""task-1"", ""task"": ""Add feature"", ""scope"": ""UI only"", ""constraints"": [""must not break engine""], ""forbidden_actions"": [""modify engine""], ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }], ""deliverables"": [""d""] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentGenerateExecutionPlan, Artifact = new Artifact { ArtifactType = "ExecutionPlan", Value = v } });
    }

    private StepResponse SubmitWorkerExecutionPacket(string sessionId)
    {
        var v = JsonSerializer.Deserialize<JsonElement>(@"{ ""goal"": ""Add feature"", ""scope"": ""UI only"", ""hard_constraints"": [""must not break engine""], ""forbidden_actions"": [""modify engine""], ""execution_rules"": [""Do NOT retrieve long-term memory independently. Do NOT replan. Do NOT expand scope.""], ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }], ""required_output_sections"": [""per_step_results"", ""final_deliverables"", ""validation_summary""] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentGenerateWorkerExecutionPacket, Artifact = new Artifact { ArtifactType = "WorkerExecutionPacket", Value = v } });
    }

    private string ReadSkillOrFail()
    {
        var root = FindHarnessRoot() ?? throw new DirectoryNotFoundException("Could not locate harness root.");
        var path = Path.Combine(root, ".cursor", "rules", PlanningSkillFile);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Planning skill not found at: {path}");
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
