using System.IO;
using System.Text.Json;
using HarnessMcp.ControlPlane;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Realistic skill-driven loop tests using the GenericAgentSimulator.
///
/// Each test uses the simulator to prove that skills + harness together
/// produce a deterministic, memory-first planning loop. The simulator
/// mirrors the semantic activation logic from 04-harness-skill-activation.mdc.
///
/// Coverage:
/// - Semantic planning intent activates harness (even without "plan" keyword)
/// - Lexical "plan" in execution context does NOT activate harness
/// - Plan mode activates harness
/// - Trivial requests do NOT activate harness
/// - Wrong stage/artifact/MCP-tool hard-stops the loop
/// - ExecutionPlan and WorkerPacket too early are rejected
/// - Context loss + resume via get-next-step works
/// - Full loop completes with required completion artifacts
/// - Raw MCP result submitted to harness is accepted
/// </summary>
public class SimulatedAgentLoopTests : IDisposable
{
    private readonly string _testSessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _stateMachine;
    private readonly GenericAgentSimulator _agent;

    public SimulatedAgentLoopTests()
    {
        _testSessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-sim-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testSessionsRoot);
        _store = new SessionStore(_testSessionsRoot);
        _stateMachine = new HarnessStateMachine(_store, new ValidationOptions());
        _agent = new GenericAgentSimulator(_stateMachine);
    }

    // ==========================================
    // Activation Tests
    // ==========================================

    [Fact]
    public void SemanticPlanningIntent_WithoutPlanKeyword_ActivatesHarnessLoop()
    {
        // "how should we approach this refactor?" has planning intent but no "plan" keyword.
        // The activation skill classifies this as planning intent semantically.
        const string request = "how should we approach this refactor?";

        request.ToLowerInvariant().Should().NotContain("plan",
            "this test verifies semantic detection works WITHOUT the word 'plan'");

        var activated = GenericAgentSimulator.HasSemanticPlanningIntent(request);
        activated.Should().BeTrue(
            "approach requests have semantic planning intent even without the literal word 'plan'");

        // Harness accepts the session and runs the full loop
        var result = _agent.RunFullLoop(request);
        result.WasActivated.Should().BeTrue();
        result.Succeeded.Should().BeTrue("harness loop completes for a planning task");
        result.FinalResponse!.Stage.Should().Be("complete");
    }

    [Fact]
    public void LexicalPlan_InExecutionContext_DoesNotActivate()
    {
        // "that's the plan — let's do it" contains the word "plan" but is execution intent.
        // The activation skill classifies this as execution-only — lexical matching is insufficient.
        const string request = "that's the plan — let's do it";

        request.ToLowerInvariant().Should().Contain("plan",
            "this test verifies that 'plan' keyword alone does NOT trigger activation");

        var activated = GenericAgentSimulator.HasSemanticPlanningIntent(request);
        activated.Should().BeFalse(
            "lexical 'plan' in execution context must NOT activate harness — only semantic intent does");

        var result = _agent.RunFullLoop(request);
        result.WasActivated.Should().BeFalse(
            "execution directive with 'plan' keyword must not activate the planning harness loop");
    }

    [Fact]
    public void PlanMode_ActivatesHarnessLoop()
    {
        // "Claude is in plan mode" is an explicit plan-mode trigger.
        // The activation skill treats plan mode as always-active planning intent.
        const string request = "Claude is in plan mode — design the implementation for the new auth module";

        var activated = GenericAgentSimulator.HasSemanticPlanningIntent(request);
        activated.Should().BeTrue(
            "plan mode is an explicit planning intent signal — always activates the harness loop");

        var result = _agent.RunFullLoop(request);
        result.WasActivated.Should().BeTrue();
        result.Succeeded.Should().BeTrue("harness loop completes for plan-mode tasks");
        result.FinalResponse!.Stage.Should().Be("complete");
    }

    [Fact]
    public void TrivialRename_DoesNotActivate()
    {
        // "rename this variable" is a trivial single-shot task with no planning intent.
        const string request = "rename this variable from oldName to newName";

        var activated = GenericAgentSimulator.HasSemanticPlanningIntent(request);
        activated.Should().BeFalse(
            "trivial rename has no planning intent — activation skill must not trigger harness");

        var result = _agent.RunFullLoop(request);
        result.WasActivated.Should().BeFalse(
            "trivial tasks must not activate the planning harness loop");
    }

    [Fact]
    public void LooksGoodProceed_DoesNotActivate()
    {
        // "looks good, proceed" is an execution approval — not planning intent.
        const string request = "looks good, proceed with the implementation";

        var activated = GenericAgentSimulator.HasSemanticPlanningIntent(request);
        activated.Should().BeFalse(
            "execution approval must not activate the harness planning loop");
    }

    [Fact]
    public void MigrationApproach_WithoutPlanKeyword_Activates()
    {
        // "design a staged rollout for this migration" has planning intent without "plan" keyword.
        const string request = "design a staged rollout for this database migration";

        request.ToLowerInvariant().Should().NotContain("plan",
            "this test verifies semantic detection for migration without the word 'plan'");

        var activated = GenericAgentSimulator.HasSemanticPlanningIntent(request);
        activated.Should().BeTrue(
            "migration + rollout is a semantic planning intent signal");

        var result = _agent.RunFullLoop(request);
        result.Succeeded.Should().BeTrue("harness completes for migration planning tasks");
    }

    // ==========================================
    // Strict Loop Tests — Wrong Stage / Wrong Artifact
    // ==========================================

    [Fact]
    public void WrongStageSubmission_AtStage1_HardStops()
    {
        // Agent tries to skip from stage 1 (need_requirement_intent)
        // directly to stage 2 (need_retrieval_chunk_set). Harness must hard-stop.
        var r0 = _agent.StartSession("design the migration approach");
        r0.Stage.Should().Be("need_requirement_intent");

        // Submit wrong action (chunk set instead of requirement intent)
        var wrongSubmit = _agent.SubmitWrongAction(
            r0.SessionId,
            HarnessActionName.AgentGenerateRetrievalChunkSet,
            "RetrievalChunkSet");

        wrongSubmit.Success.Should().BeFalse(
            "harness must hard-stop when agent submits wrong stage action");
        wrongSubmit.Stage.Should().Be("error",
            "session must be in error state after wrong action");
        wrongSubmit.NextAction.Should().Be(HarnessActionName.StopWithError);
    }

    [Fact]
    public void WrongArtifactShape_AtRequirementIntentStage_HardStops()
    {
        // Agent submits an invalid RequirementIntent shape (missing required fields).
        var r0 = _agent.StartSession("approach for refactoring the auth module");

        // Submit invalid RequirementIntent (missing task_id, task_type, etc.)
        var invalidSubmit = _agent.SubmitInvalidArtifact(
            r0.SessionId,
            HarnessActionName.AgentGenerateRequirementIntent,
            @"{ ""summary"": ""just a summary"" }");

        invalidSubmit.Success.Should().BeFalse(
            "harness must hard-stop on invalid artifact shape at stage 1");
        invalidSubmit.Stage.Should().Be("error");
        invalidSubmit.Errors.Should().NotBeEmpty(
            "harness must return field-level error messages for invalid artifact");
    }

    [Fact]
    public void WrongMcpTool_SubmittedResult_HardStops()
    {
        // Harness is at need_mcp_retrieve_memory_by_chunks.
        // Agent submits merge results instead of retrieve results. Harness must hard-stop.
        var r0 = _agent.StartSession("design the rollout strategy");
        var r3 = _agent.AdvanceTo(r0.SessionId, HarnessActionName.AgentCallMcpRetrieveMemoryByChunks);
        r3.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");

        // Submit MERGE action instead of RETRIEVE action — wrong MCP tool
        var wrongMcpSubmit = _agent.SubmitWrongAction(
            r0.SessionId,
            HarnessActionName.AgentCallMcpMergeRetrievalResults,
            "MergeRetrievalResultsResponse");

        wrongMcpSubmit.Success.Should().BeFalse(
            "harness must hard-stop when agent submits wrong MCP tool result");
        wrongMcpSubmit.Stage.Should().Be("error");
    }

    [Fact]
    public void ExecutionPlan_SubmittedTooEarly_IsRejected()
    {
        // Agent tries to submit an ExecutionPlan at stage 2 (before MCP stages).
        // Harness must reject — ExecutionPlan is only valid at need_execution_plan.
        var r0 = _agent.StartSession("outline the implementation steps");
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentGenerateRequirementIntent);
        // Now at need_retrieval_chunk_set

        var earlyPlan = _agent.SubmitWrongAction(
            r0.SessionId,
            HarnessActionName.AgentGenerateExecutionPlan,
            "ExecutionPlan");

        earlyPlan.Success.Should().BeFalse(
            "ExecutionPlan submitted before need_execution_plan must be rejected");
        earlyPlan.Stage.Should().Be("error");
    }

    [Fact]
    public void WorkerPacket_SubmittedTooEarly_IsRejected()
    {
        // Agent tries to submit a WorkerExecutionPacket at stage 1.
        // Harness must reject — WorkerPacket is only valid at need_worker_execution_packet.
        var r0 = _agent.StartSession("strategy for refactoring the service");

        var earlyPacket = _agent.SubmitWrongAction(
            r0.SessionId,
            HarnessActionName.AgentGenerateWorkerExecutionPacket,
            "WorkerExecutionPacket");

        earlyPacket.Success.Should().BeFalse(
            "WorkerExecutionPacket submitted before need_worker_execution_packet must be rejected");
        earlyPacket.Stage.Should().Be("error");
    }

    [Fact]
    public void McpBeforeHarnessRequests_IsRejected()
    {
        // Agent submits MCP retrieve result before harness reaches MCP stage.
        var r0 = _agent.StartSession("migration approach for the database");
        // At need_requirement_intent — MCP not valid yet

        var prematureMcp = _agent.SubmitWrongAction(
            r0.SessionId,
            HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            "RetrieveMemoryByChunksResponse");

        prematureMcp.Success.Should().BeFalse(
            "MCP tool result submitted before harness reaches MCP stage must be rejected");
        prematureMcp.Stage.Should().Be("error");
    }

    // ==========================================
    // Resume Tests — Context Loss and Re-Sync
    // ==========================================

    [Fact]
    public void LostContext_Resume_ViaGetNextStep_Succeeds()
    {
        // Start session and advance to stage 2. Then simulate context loss.
        // Use get-next-step to re-sync and continue.
        var r0 = _agent.StartSession("decompose the implementation into steps");
        r0.Stage.Should().Be("need_requirement_intent");

        // Submit stage 1
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentGenerateRequirementIntent)
              .Stage.Should().Be("need_retrieval_chunk_set");

        // --- Simulate context loss ---
        // Agent uses get-next-step to re-sync (mirrors 00-harness-control-plane.mdc resume behavior)
        var resync = _agent.GetNextStep(r0.SessionId);
        resync.Success.Should().BeTrue("get-next-step must succeed on a valid session");
        resync.Stage.Should().Be("need_retrieval_chunk_set",
            "get-next-step must return the same stage the session is at");
        resync.NextAction.Should().Be(HarnessActionName.AgentGenerateRetrievalChunkSet,
            "get-next-step must return the correct required action");

        // Continue from re-synced state — submit remaining stages
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentGenerateRetrievalChunkSet).Success.Should().BeTrue();
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentValidateChunkQuality).Success.Should().BeTrue();
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentCallMcpRetrieveMemoryByChunks).Success.Should().BeTrue();
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentCallMcpMergeRetrievalResults).Success.Should().BeTrue();
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentCallMcpBuildMemoryContextPack).Success.Should().BeTrue();
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentGenerateExecutionPlan).Success.Should().BeTrue();
        var final = _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentGenerateWorkerExecutionPacket);

        final.Stage.Should().Be("complete", "loop continues correctly after resume via get-next-step");
    }

    [Fact]
    public void LostContext_GetSessionStatus_ReturnsCorrectState()
    {
        // After context loss, agent uses get-session-status to inspect state.
        var r0 = _agent.StartSession("investigate the architecture before changing it");
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentGenerateRequirementIntent);
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentGenerateRetrievalChunkSet);

        // Simulate context loss — use get-session-status to inspect state
        var status = _agent.GetSessionStatus(r0.SessionId);
        status.Success.Should().BeTrue("get-session-status must succeed for a valid session");
        status.Stage.Should().Be("need_retrieval_chunk_validation",
            "get-session-status must report the accurate current stage");
        status.AcceptedArtifacts.Should().Contain(a => a.Contains("RequirementIntent"),
            "get-session-status must list accepted artifacts");
    }

    // ==========================================
    // Skills + Harness Proof Tests
    // ==========================================

    [Fact]
    public void FullLoop_CompletesWithRequiredArtifacts_ViaSimulator()
    {
        // Full happy path via simulator — proves skills + harness produce complete planning loop.
        var result = _agent.RunFullLoop("design an approach to add year switching without changing engine logic");

        result.WasActivated.Should().BeTrue("approach request activates planning mode");
        result.Succeeded.Should().BeTrue("full harness loop must complete");
        result.FinalResponse!.Stage.Should().Be("complete");
        result.FinalResponse.CompletionArtifacts.Should().NotBeNull(
            "completion response must include both final artifacts");
        result.FinalResponse.CompletionArtifacts!.ExecutionPlan.Should().NotBeNull(
            "ExecutionPlan must be in completion artifacts");
        result.FinalResponse.CompletionArtifacts.WorkerExecutionPacket.Should().NotBeNull(
            "WorkerExecutionPacket must be in completion artifacts");
    }

    [Fact]
    public void FreeFormSkip_ToComplete_IsRejected()
    {
        // Agent tries to jump directly to complete without going through stages.
        // Harness must reject — cannot reach complete without all stages.
        var r0 = _agent.StartSession("strategy for the migration rollout");

        var skipToComplete = _agent.SubmitWrongAction(
            r0.SessionId,
            HarnessActionName.Complete,
            "Complete");

        skipToComplete.Success.Should().BeFalse(
            "cannot reach complete by skipping all stages — harness must reject");
        skipToComplete.Stage.Should().Be("error");
    }

    [Fact]
    public void HarnessProvides_ExactMcpToolName_ForAgent()
    {
        // Harness must provide exact tool name and payload.request at each MCP stage.
        // This verifies that the harness gives the agent exactly what 03-harness-mcp-tool-calling.mdc says it will.
        var r0 = _agent.StartSession("approach for the authentication refactor");
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentGenerateRequirementIntent);
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentGenerateRetrievalChunkSet);
        var r3 = _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentValidateChunkQuality);

        // Verify harness returns exact tool name and payload.request at retrieve stage
        r3.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");
        r3.ToolName.Should().Be("retrieve_memory_by_chunks",
            "harness must return exact tool name as specified in 03-harness-mcp-tool-calling.mdc");
        r3.Payload.ValueKind.Should().Be(JsonValueKind.Object);
        r3.Payload.TryGetProperty("request", out _).Should().BeTrue();

        // Submit RAW MCP result — harness accepts it and provides next tool name
        var r4 = _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentCallMcpRetrieveMemoryByChunks);
        r4.Stage.Should().Be("need_mcp_merge_retrieval_results");
        r4.ToolName.Should().Be("merge_retrieval_results",
            "harness must return exact tool name for merge stage");

        var r5 = _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentCallMcpMergeRetrievalResults);
        r5.Stage.Should().Be("need_mcp_build_memory_context_pack");
        r5.ToolName.Should().Be("build_memory_context_pack",
            "harness must return exact tool name for context pack stage");
    }

    [Fact]
    public void RawMcpResult_SubmittedToHarness_IsAccepted()
    {
        // Agent submits the RAW MCP retrieve result to harness.
        // Harness must accept it and advance to the merge stage.
        var r0 = _agent.StartSession("design the rollout approach");
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentGenerateRequirementIntent);
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentGenerateRetrievalChunkSet);
        _agent.SubmitValidArtifact(r0.SessionId, HarnessActionName.AgentValidateChunkQuality);

        // Submit the canonical raw MCP retrieve result (as returned by the MCP tool)
        var rawMcpResult = new Artifact
        {
            ArtifactType = "RetrieveMemoryByChunksResponse",
            Value = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{
                ""task_id"": ""task-1"",
                ""chunk_results"": [{
                    ""chunk_id"": ""c1"",
                    ""chunk_type"": ""core_task"",
                    ""results"": {
                        ""decisions"": [],
                        ""best_practices"": [{ ""knowledge_item_id"": ""k1"", ""title"": ""t"", ""summary"": ""s"" }],
                        ""anti_patterns"": [],
                        ""similar_cases"": [],
                        ""constraints"": [],
                        ""references"": [],
                        ""structures"": []
                    }
                }]
            }")
        };

        var r4 = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = rawMcpResult
        });

        r4.Success.Should().BeTrue("raw MCP retrieve result submitted to harness must be accepted");
        r4.Stage.Should().Be("need_mcp_merge_retrieval_results",
            "harness advances to merge stage after accepting retrieve result");
    }

    [Fact]
    public void ErrorState_SessionLocked_CannotContinue()
    {
        // Once harness enters error state, the session is locked.
        // The simulated agent cannot continue without resolving the error.
        var r0 = _agent.StartSession("decompose this into implementation steps");

        // Force error by submitting wrong action
        var errorResult = _agent.SubmitWrongAction(
            r0.SessionId,
            HarnessActionName.AgentGenerateRetrievalChunkSet,
            "RetrievalChunkSet");
        errorResult.Stage.Should().Be("error");

        // Try to continue — harness must reject because session is in error state
        var continueAttempt = _agent.SubmitWrongAction(
            r0.SessionId,
            HarnessActionName.AgentGenerateRequirementIntent,
            "RequirementIntent");
        continueAttempt.Success.Should().BeFalse(
            "session in error state must lock all further submissions until resolved");
    }

    [Fact]
    public void MultipleSemanticRequests_EachActivatesIndependently()
    {
        // Each semantic planning request gets its own independent harness session.
        // Simulates multiple planning rounds without state bleeding.
        var requests = new[]
        {
            "how should we approach this refactor?",
            "strategy for the database migration",
            "design the rollout for this feature"
        };

        foreach (var request in requests)
        {
            var activated = GenericAgentSimulator.HasSemanticPlanningIntent(request);
            activated.Should().BeTrue($"'{request}' has semantic planning intent");

            var result = _agent.RunFullLoop(request);
            result.WasActivated.Should().BeTrue();
            result.Succeeded.Should().BeTrue($"full loop completes for: {request}");
            result.FinalResponse!.Stage.Should().Be("complete");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_testSessionsRoot))
            Directory.Delete(_testSessionsRoot, true);
    }
}
