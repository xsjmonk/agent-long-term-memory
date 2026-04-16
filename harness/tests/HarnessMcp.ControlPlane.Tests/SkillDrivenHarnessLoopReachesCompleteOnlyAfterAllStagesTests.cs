using System.IO;
using System.Text.Json;
using FluentAssertions;
using HarnessMcp.ControlPlane;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Proves that the skill-driven harness loop reaches "complete" state ONLY after all 9 stages
/// have been submitted successfully, and that no shortcut can produce a completion response.
///
/// Two-layer proof:
///   1. Skill-content: planning skill (00-harness-control-plane.mdc) contains a "what to present at
///      completion" section, mandates all stages via the stage table, and prohibits skipping/batching.
///   2. Harness behavior: attempting to complete before all stages fails; happy-path through all
///      stages is the ONLY path to complete state; completion response includes both artifacts.
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class SkillDrivenHarnessLoopReachesCompleteOnlyAfterAllStagesTests : IDisposable
{
    private readonly string _sessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _sm;
    private const string PlanningSkillFile = "00-harness-control-plane.mdc";

    public SkillDrivenHarnessLoopReachesCompleteOnlyAfterAllStagesTests()
    {
        _sessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-complete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sessionsRoot);
        _store = new SessionStore(_sessionsRoot);
        _sm = new HarnessStateMachine(_store, new ValidationOptions());
    }

    // ==========================================
    // Layer 1: Planning skill documents the completion requirement
    // ==========================================

    [Fact]
    public void PlanningSkill_ContainsWhatToPresentAtCompletion_Section()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("what to present",
            "planning skill must include a 'what to present at completion' section — agents need to know what to surface when done");
    }

    [Fact]
    public void PlanningSkill_ContainsAllStages_InStageTable()
    {
        var content = ReadSkillOrFail();
        // All 9 stages must be in the table
        content.Should().Contain("need_requirement_intent");
        content.Should().Contain("need_retrieval_chunk_set");
        content.Should().Contain("need_retrieval_chunk_validation");
        content.Should().Contain("need_mcp_retrieve_memory_by_chunks");
        content.Should().Contain("need_mcp_merge_retrieval_results");
        content.Should().Contain("need_mcp_build_memory_context_pack");
        content.Should().Contain("need_execution_plan");
        content.Should().Contain("need_worker_execution_packet");
        content.Should().Contain("complete");
    }

    [Fact]
    public void PlanningSkill_MandatesAllStages_ViaForbiddenWording()
    {
        var content = ReadSkillOrFail();
        content.Should().Contain("FORBIDDEN",
            "planning skill must use FORBIDDEN to prohibit stage skipping — completion requires all stages");
    }

    [Fact]
    public void PlanningSkill_ContainsPlanningAndImplementationScenario()
    {
        var content = ReadSkillOrFail();
        // Must address what to do when user asks for both planning AND implementation at once
        content.ToLowerInvariant().Should().Contain("implementation in the same message",
            "planning skill must explicitly address planning+implementation-in-same-message scenario");
    }

    // ==========================================
    // Layer 2: Harness enforces that complete state is only reachable after all stages
    // ==========================================

    [Fact]
    public void Harness_CannotComplete_AfterOnlyRequirementIntent()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        SubmitRequirementIntent(r0.SessionId);
        // Session is at need_retrieval_chunk_set, NOT complete

        var jump = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.Complete,
            Artifact = new Artifact { ArtifactType = "Complete", Value = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement("{}") }
        });

        jump.Success.Should().BeFalse("cannot complete after only 1 stage");
        jump.Stage.Should().Be("error");
    }

    [Fact]
    public void Harness_CannotComplete_AfterMcpStages_WithoutExecutionPlan()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        SubmitRequirementIntent(r0.SessionId);
        SubmitRetrievalChunkSet(r0.SessionId);
        SubmitChunkQualityReport(r0.SessionId);
        SubmitRetrieveMemoryByChunksResponse(r0.SessionId);
        SubmitMergeRetrievalResultsResponse(r0.SessionId);
        var r6 = SubmitBuildMemoryContextPackResponse(r0.SessionId);
        r6.Stage.Should().Be("need_execution_plan");

        // Session is at need_execution_plan, NOT complete
        var jump = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.Complete,
            Artifact = new Artifact { ArtifactType = "Complete", Value = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement("{}") }
        });

        jump.Success.Should().BeFalse("cannot complete without ExecutionPlan and WorkerExecutionPacket");
        jump.Stage.Should().Be("error");
    }

    [Fact]
    public void Harness_CannotComplete_AfterExecutionPlan_WithoutWorkerPacket()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        SubmitRequirementIntent(r0.SessionId);
        SubmitRetrievalChunkSet(r0.SessionId);
        SubmitChunkQualityReport(r0.SessionId);
        SubmitRetrieveMemoryByChunksResponse(r0.SessionId);
        SubmitMergeRetrievalResultsResponse(r0.SessionId);
        SubmitBuildMemoryContextPackResponse(r0.SessionId);
        var r7 = SubmitExecutionPlan(r0.SessionId);
        r7.Stage.Should().Be("need_worker_execution_packet");

        // Jump to complete — must fail, still needs WorkerExecutionPacket
        var jump = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.Complete,
            Artifact = new Artifact { ArtifactType = "Complete", Value = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement("{}") }
        });

        jump.Success.Should().BeFalse("cannot complete without WorkerExecutionPacket");
        jump.Stage.Should().Be("error");
    }

    [Fact]
    public void Harness_HappyPath_ReachesComplete_OnlyAfterAllEightSubmits()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });

        // All 8 stage submissions required
        SubmitRequirementIntent(r0.SessionId).Stage.Should().Be("need_retrieval_chunk_set");
        SubmitRetrievalChunkSet(r0.SessionId).Stage.Should().Be("need_retrieval_chunk_validation");
        SubmitChunkQualityReport(r0.SessionId).Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");
        SubmitRetrieveMemoryByChunksResponse(r0.SessionId).Stage.Should().Be("need_mcp_merge_retrieval_results");
        SubmitMergeRetrievalResultsResponse(r0.SessionId).Stage.Should().Be("need_mcp_build_memory_context_pack");
        SubmitBuildMemoryContextPackResponse(r0.SessionId).Stage.Should().Be("need_execution_plan");
        SubmitExecutionPlan(r0.SessionId).Stage.Should().Be("need_worker_execution_packet");

        var final = SubmitWorkerExecutionPacket(r0.SessionId);

        // Only NOW is the session complete
        final.Success.Should().BeTrue("all 8 stages completed — harness must reach complete");
        final.Stage.Should().Be("complete");
        final.NextAction.Should().Be(HarnessActionName.Complete);
    }

    [Fact]
    public void Harness_CompletionResponse_IncludesBothFinalArtifacts()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        SubmitRequirementIntent(r0.SessionId);
        SubmitRetrievalChunkSet(r0.SessionId);
        SubmitChunkQualityReport(r0.SessionId);
        SubmitRetrieveMemoryByChunksResponse(r0.SessionId);
        SubmitMergeRetrievalResultsResponse(r0.SessionId);
        SubmitBuildMemoryContextPackResponse(r0.SessionId);
        SubmitExecutionPlan(r0.SessionId);
        var final = SubmitWorkerExecutionPacket(r0.SessionId);

        // Completion response must include both artifacts for the agent to present
        final.CompletionArtifacts.Should().NotBeNull(
            "planning skill mandates presenting both artifacts at completion — harness must return them");
        final.CompletionArtifacts!.ExecutionPlan.Should().NotBeNull(
            "completion artifacts must include ExecutionPlan");
        final.CompletionArtifacts.WorkerExecutionPacket.Should().NotBeNull(
            "completion artifacts must include WorkerExecutionPacket");
    }

    [Fact]
    public void Harness_CompletionResponse_ListsAllAcceptedArtifacts()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        SubmitRequirementIntent(r0.SessionId);
        SubmitRetrievalChunkSet(r0.SessionId);
        SubmitChunkQualityReport(r0.SessionId);
        SubmitRetrieveMemoryByChunksResponse(r0.SessionId);
        SubmitMergeRetrievalResultsResponse(r0.SessionId);
        SubmitBuildMemoryContextPackResponse(r0.SessionId);
        SubmitExecutionPlan(r0.SessionId);
        var final = SubmitWorkerExecutionPacket(r0.SessionId);

        final.AcceptedArtifacts.Should().Contain("RequirementIntent");
        final.AcceptedArtifacts.Should().Contain("ExecutionPlan");
        final.AcceptedArtifacts.Should().Contain("WorkerExecutionPacket");
    }

    [Fact]
    public void Harness_CompleteStage_CannotAdvanceFurther()
    {
        // After completion, the harness is in "complete" state — there's nowhere further to advance
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design something" });
        SubmitRequirementIntent(r0.SessionId);
        SubmitRetrievalChunkSet(r0.SessionId);
        SubmitChunkQualityReport(r0.SessionId);
        SubmitRetrieveMemoryByChunksResponse(r0.SessionId);
        SubmitMergeRetrievalResultsResponse(r0.SessionId);
        SubmitBuildMemoryContextPackResponse(r0.SessionId);
        SubmitExecutionPlan(r0.SessionId);
        SubmitWorkerExecutionPacket(r0.SessionId); // completes

        // Try to submit another action after complete — must either fail or be gracefully rejected
        var afterComplete = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact
            {
                ArtifactType = "RequirementIntent",
                Value = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-2"", ""task_type"": ""ui"", ""goal"": ""g"", ""hard_constraints"": [], ""risk_signals"": [], ""complexity"": ""low"" }")
            }
        });

        // Complete state has no expected action, so wrong action is the guaranteed outcome
        afterComplete.Success.Should().BeFalse("cannot advance a completed session — harness must not accept further submissions");
    }

    // --- Helpers ---

    private StepResponse SubmitRequirementIntent(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-1"", ""task_type"": ""ui-change"", ""goal"": ""implement feature"", ""hard_constraints"": [], ""risk_signals"": [], ""complexity"": ""low"" }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentGenerateRequirementIntent, Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = v } });
    }

    private StepResponse SubmitRetrievalChunkSet(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-1"", ""complexity"": ""low"", ""chunks"": [{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""implement"" }] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet, Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = v } });
    }

    private StepResponse SubmitChunkQualityReport(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""isValid"": true, ""has_core_task"": true, ""has_constraint"": false, ""has_risk"": false, ""has_pattern"": false, ""has_similar_case"": false, ""errors"": [], ""warnings"": [] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentValidateChunkQuality, Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = v } });
    }

    private StepResponse SubmitRetrieveMemoryByChunksResponse(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-1"", ""chunk_results"": [{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""results"": { ""decisions"": [], ""best_practices"": [{ ""knowledge_item_id"": ""k1"", ""title"": ""t"", ""summary"": ""s"" }], ""anti_patterns"": [], ""similar_cases"": [], ""constraints"": [], ""references"": [], ""structures"": [] } }] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks, Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = v } });
    }

    private StepResponse SubmitMergeRetrievalResultsResponse(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-1"", ""merged"": { ""decisions"": [], ""constraints"": [], ""best_practices"": [{ ""item"": { ""knowledge_item_id"": ""k1"", ""title"": ""t"", ""summary"": ""s"" }, ""supported_by_chunk_ids"": [""c1""], ""supported_by_chunk_types"": [""core_task""], ""merge_rationales"": [""relevant""] }], ""anti_patterns"": [], ""similar_cases"": [], ""references"": [], ""structures"": [] } }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentCallMcpMergeRetrievalResults, Artifact = new Artifact { ArtifactType = "MergeRetrievalResultsResponse", Value = v } });
    }

    private StepResponse SubmitBuildMemoryContextPackResponse(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-1"", ""memory_context_pack"": { ""must_follow"": [], ""best_practices"": [], ""avoid"": [], ""similar_case_guidance"": [], ""retrieval_support"": { ""multi_supported_items"": [], ""single_route_important_items"": [] } } }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentCallMcpBuildMemoryContextPack, Artifact = new Artifact { ArtifactType = "BuildMemoryContextPackResponse", Value = v } });
    }

    private StepResponse SubmitExecutionPlan(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-1"", ""task"": ""Add feature"", ""scope"": ""UI only"", ""constraints"": [""must not break engine""], ""forbidden_actions"": [""modify engine""], ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }], ""deliverables"": [""d""] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest { SessionId = sessionId, CompletedAction = HarnessActionName.AgentGenerateExecutionPlan, Artifact = new Artifact { ArtifactType = "ExecutionPlan", Value = v } });
    }

    private StepResponse SubmitWorkerExecutionPacket(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""goal"": ""Add feature"", ""scope"": ""UI only"", ""hard_constraints"": [""must not break engine""], ""forbidden_actions"": [""modify engine""], ""execution_rules"": [""Do NOT retrieve long-term memory independently. Do NOT replan. Do NOT expand scope.""], ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }], ""required_output_sections"": [""per_step_results"", ""final_deliverables"", ""validation_summary""] }");
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
