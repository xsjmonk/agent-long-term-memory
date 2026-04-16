using System.IO;
using System.Text.Json;
using HarnessMcp.ControlPlane;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Integration-style tests that simulate the full skill-driven harness planning loop.
/// Each test proves both that the relevant skill contains the correct operational guidance
/// AND that the harness state machine enforces the corresponding behavior at runtime.
///
/// The 8 SkillDrivenLoop_ tests map to the Strict Final Prompt requirements.
/// The 5 skill strength tests verify production-grade operational runbook quality.
///
/// Implementation is NOT complete until all tests in this file pass.
/// </summary>
public class SkillDrivenLoopIntegrationTests : IDisposable
{
    private readonly string _testSessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _stateMachine;

    public SkillDrivenLoopIntegrationTests()
    {
        _testSessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-sdl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testSessionsRoot);
        _store = new SessionStore(_testSessionsRoot);
        _stateMachine = new HarnessStateMachine(_store, new ValidationOptions());
    }

    // ==========================================
    // File-reading helpers (self-contained)
    // ==========================================

    private static string ReadRuleOrFail(string fileName)
    {
        var harnessRoot = FindHarnessRootOrFail();
        var path = Path.Combine(harnessRoot, "agent-rules", fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required canonical rule file '{fileName}' not found at: {path}");
        return File.ReadAllText(path);
    }

    private static string FindHarnessRootOrFail()
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
        throw new DirectoryNotFoundException("Could not locate harness repository root.");
    }

    // ==========================================
    // Artifact helpers (self-contained for test isolation)
    // ==========================================

    private StepResponse StartSession(string rawTask)
        => _stateMachine.StartSession(new StartSessionRequest { RawTask = rawTask });

    private StepResponse SubmitRequirementIntent(string sessionId,
        string[]? hardConstraints = null, string[]? riskSignals = null)
    {
        var hcJson = string.Join(",", (hardConstraints ?? Array.Empty<string>()).Select(c => $"\"{c}\""));
        var rsJson = string.Join(",", (riskSignals ?? Array.Empty<string>()).Select(r => $"\"{r}\""));
        var intent = HarnessJson.ParseJsonElement($@"{{
            ""task_id"": ""task-1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""implement new feature"",
            ""hard_constraints"": [{hcJson}],
            ""risk_signals"": [{rsJson}],
            ""complexity"": ""low""
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
        var chunkSet = HarnessJson.ParseJsonElement(@"{
            ""task_id"": ""task-1"",
            ""complexity"": ""low"",
            ""chunks"": [{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""implement the feature"" }]
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
        var report = HarnessJson.ParseJsonElement(@"{
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

    private StepResponse SubmitRetrieveMemoryByChunksResponse(string sessionId)
    {
        var response = HarnessJson.ParseJsonElement(@"{
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
        }");
        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = response }
        });
    }

    private StepResponse SubmitMergeRetrievalResultsResponse(string sessionId)
    {
        var response = HarnessJson.ParseJsonElement(@"{
            ""task_id"": ""task-1"",
            ""merged"": {
                ""decisions"": [],
                ""constraints"": [],
                ""best_practices"": [{ ""item"": { ""knowledge_item_id"": ""k1"", ""title"": ""t"", ""summary"": ""s"" }, ""supported_by_chunk_ids"": [""c1""], ""supported_by_chunk_types"": [""core_task""], ""merge_rationales"": [""relevant""] }],
                ""anti_patterns"": [],
                ""similar_cases"": [],
                ""references"": [],
                ""structures"": []
            }
        }");
        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpMergeRetrievalResults,
            Artifact = new Artifact { ArtifactType = "MergeRetrievalResultsResponse", Value = response }
        });
    }

    private StepResponse SubmitBuildMemoryContextPackResponse(string sessionId)
    {
        var response = HarnessJson.ParseJsonElement(@"{
            ""task_id"": ""task-1"",
            ""memory_context_pack"": {
                ""must_follow"": [],
                ""best_practices"": [],
                ""avoid"": [],
                ""similar_case_guidance"": [],
                ""retrieval_support"": { ""multi_supported_items"": [], ""single_route_important_items"": [] }
            }
        }");
        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpBuildMemoryContextPack,
            Artifact = new Artifact { ArtifactType = "BuildMemoryContextPackResponse", Value = response }
        });
    }

    private StepResponse SubmitExecutionPlan(string sessionId)
    {
        var plan = HarnessJson.ParseJsonElement(@"{
            ""task_id"": ""task-1"",
            ""task"": ""Add feature to UI layer"",
            ""scope"": ""UI layer only"",
            ""constraints"": [""must not change engine""],
            ""forbidden_actions"": [""modify engine files"", ""change database schema""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""Create UI component"", ""actions"": [""Add new component file""], ""outputs"": [""Component file created""], ""acceptance_checks"": [""Component renders without errors""] }],
            ""deliverables"": [""New UI component""]
        }");
        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateExecutionPlan,
            Artifact = new Artifact { ArtifactType = "ExecutionPlan", Value = plan }
        });
    }

    private StepResponse SubmitWorkerExecutionPacket(string sessionId)
    {
        var packet = HarnessJson.ParseJsonElement(@"{
            ""goal"": ""Add feature to UI layer"",
            ""scope"": ""UI layer only"",
            ""hard_constraints"": [""must not change engine""],
            ""forbidden_actions"": [""modify engine files"", ""change database schema""],
            ""execution_rules"": [""Do NOT retrieve long-term memory independently. Do NOT replan. Do NOT expand scope.""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""Create UI component"", ""actions"": [""Add new component file""], ""outputs"": [""Component file created""], ""acceptance_checks"": [""Component renders without errors""] }],
            ""required_output_sections"": [""per_step_results"", ""final_deliverables"", ""validation_summary""]
        }");
        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateWorkerExecutionPacket,
            Artifact = new Artifact { ArtifactType = "WorkerExecutionPacket", Value = packet }
        });
    }

    // ==========================================
    // 8 Named SkillDrivenLoop_ Integration Tests
    // ==========================================

    [Fact]
    public void SkillDrivenLoop_SemanticPlanningIntent_ActivatesHarnessFlow()
    {
        // SKILL PROOF: Activation skill requires semantic planning intent — not keyword detection
        var activationSkill = ReadRuleOrFail("04-harness-skill-activation.mdc");
        activationSkill.Should().Contain("semantic",
            "activation skill must detect semantic planning intent");
        activationSkill.Should().Contain("planning intent",
            "activation skill must use the phrase 'planning intent'");
        activationSkill.Should().Contain("not lexical",
            "activation skill must explicitly state activation is not lexical-only");
        activationSkill.ToLowerInvariant().Should().Contain("00-harness-control-plane",
            "activation skill must route planning intent to the harness planning loop");

        // HARNESS PROOF: Harness accepts a planning task and starts the 9-stage loop
        var r0 = StartSession("Design an approach to refactor the auth module");
        r0.Success.Should().BeTrue("harness must accept a planning session");
        r0.Stage.Should().Be("need_requirement_intent",
            "harness must begin at need_requirement_intent — the first canonical stage");
        r0.NextAction.Should().Be(HarnessActionName.AgentGenerateRequirementIntent,
            "harness must instruct the agent to generate requirement intent first");

        // Full happy path confirms the loop completes for a semantic planning task
        SubmitRequirementIntent(r0.SessionId).Success.Should().BeTrue();
        SubmitRetrievalChunkSet(r0.SessionId).Success.Should().BeTrue();
        SubmitChunkQualityReport(r0.SessionId).Success.Should().BeTrue();
        SubmitRetrieveMemoryByChunksResponse(r0.SessionId).Success.Should().BeTrue();
        SubmitMergeRetrievalResultsResponse(r0.SessionId).Success.Should().BeTrue();
        SubmitBuildMemoryContextPackResponse(r0.SessionId).Success.Should().BeTrue();
        SubmitExecutionPlan(r0.SessionId).Success.Should().BeTrue();
        var final = SubmitWorkerExecutionPacket(r0.SessionId);
        final.Stage.Should().Be("complete",
            "harness reaches complete only after all planning stages are accepted");
    }

    [Fact]
    public void SkillDrivenLoop_ExecutionIntent_DoesNotActivatePlanningFlow()
    {
        // SKILL PROOF: Activation skill explicitly covers execution-intent non-activation cases
        var activationSkill = ReadRuleOrFail("04-harness-skill-activation.mdc");
        activationSkill.Should().Contain("do NOT activate",
            "activation skill must have explicit do-NOT-activate wording");
        activationSkill.Should().Contain("let's do it",
            "activation skill must cover 'let's do it' as a non-activation example");
        activationSkill.Should().Contain("implement it now",
            "activation skill must cover 'implement it now' as a non-activation example");
        activationSkill.Should().Contain("looks good",
            "activation skill must cover 'looks good, proceed' as a non-activation example");
        activationSkill.ToLowerInvariant().Should().Contain("lexical-only",
            "activation skill must state that lexical-only matching produces false positives");

        // HARNESS PROOF: The harness itself has no activation gate — it accepts any start-session call.
        // This proves that the activation skill (04) is the sole gate that prevents execution-intent
        // requests from entering the planning loop. If the skill does not activate, start-session
        // is never called, and the harness never runs.
        var r0 = StartSession("implement it now — execute the accepted plan");
        r0.Success.Should().BeTrue(
            "harness has no activation guard — it accepts any session string. " +
            "The activation skill is the sole gate preventing execution intents from reaching here.");
        r0.Stage.Should().Be("need_requirement_intent",
            "harness always starts at stage 1 for any session — skill is the only gate");
    }

    [Fact]
    public void SkillDrivenLoop_HarnessIsOnlyPlanningEntrypoint()
    {
        // SKILL PROOF: Planning skill mandates harness as the ONLY entrypoint — operational imperatives required
        var planningSkill = ReadRuleOrFail("00-harness-control-plane.mdc");
        planningSkill.Should().Contain("ALWAYS",
            "planning skill must use ALWAYS imperatives — harness is always the entrypoint");
        planningSkill.Should().Contain("MUST",
            "planning skill must use MUST — the harness-first requirement is mandatory");
        planningSkill.Should().Contain("FORBIDDEN",
            "planning skill must use FORBIDDEN — bypassing harness is explicitly forbidden");
        planningSkill.Should().Contain("start-session",
            "planning skill must identify start-session as the first harness call");
        planningSkill.Should().Contain("NEVER skip stages",
            "planning skill must prohibit skipping harness stages");

        // HARNESS PROOF: Harness controls the entire stage sequence — agent cannot choose next action
        var r0 = StartSession("Plan the database migration");
        r0.Stage.Should().Be("need_requirement_intent");
        r0.NextAction.Should().Be(HarnessActionName.AgentGenerateRequirementIntent,
            "harness controls what the agent does next — not the agent");

        var r1 = SubmitRequirementIntent(r0.SessionId);
        r1.Stage.Should().Be("need_retrieval_chunk_set",
            "harness controls stage transitions — agent cannot bypass or reorder");

        var r2 = SubmitRetrievalChunkSet(r0.SessionId);
        r2.Stage.Should().Be("need_retrieval_chunk_validation",
            "harness always controls next stage — planning cannot proceed outside the harness");
    }

    [Fact]
    public void SkillDrivenLoop_CannotSkipStages()
    {
        // SKILL PROOF: Planning skill explicitly prohibits skipping stages
        var planningSkill = ReadRuleOrFail("00-harness-control-plane.mdc");
        planningSkill.Should().Contain("NEVER skip stages",
            "planning skill must contain explicit NEVER-skip-stages prohibition");
        planningSkill.ToLowerInvariant().Should().Contain("do-not-skip",
            "planning skill must have a dedicated do-not-skip section");

        // HARNESS PROOF: Skipping from stage 1 directly to stage 2 is hard-stopped
        var r0 = StartSession("Design migration approach — skip test 1");
        r0.Stage.Should().Be("need_requirement_intent");

        var skipStage2 = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = HarnessJson.ParseJsonElement("{}") }
        });
        skipStage2.Success.Should().BeFalse("cannot skip requirement intent — must submit in order");
        skipStage2.Stage.Should().Be("error");

        // Skipping MCP stages to jump to execution plan is also hard-stopped
        var r1 = StartSession("Design migration approach — skip test 2");
        SubmitRequirementIntent(r1.SessionId);
        SubmitRetrievalChunkSet(r1.SessionId);
        var r3 = SubmitChunkQualityReport(r1.SessionId);
        r3.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");

        var skipToExecPlan = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r1.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateExecutionPlan,
            Artifact = new Artifact { ArtifactType = "ExecutionPlan", Value = HarnessJson.ParseJsonElement("{}") }
        });
        skipToExecPlan.Success.Should().BeFalse("cannot skip MCP stages to jump to execution plan");
        skipToExecPlan.Stage.Should().Be("error");
    }

    [Fact]
    public void SkillDrivenLoop_CannotCallMcpBeforeHarnessRequestsIt()
    {
        // SKILL PROOF: MCP skill prohibits calling MCP before harness reaches the MCP stage
        var mcpSkill = ReadRuleOrFail("03-harness-mcp-tool-calling.mdc");
        mcpSkill.Should().Contain("retrieve_memory_by_chunks",
            "MCP skill must name the exact first MCP tool");
        mcpSkill.Should().Contain("NEVER",
            "MCP skill must have NEVER prohibitions");
        mcpSkill.Should().Contain("Do NOT call MCP at other times",
            "MCP skill must explicitly prohibit calling MCP outside harness-instructed stages");

        // HARNESS PROOF: MCP submission at stage 1 (before MCP stage) is hard-stopped
        var r0 = StartSession("Design migration — premature MCP test 1");
        r0.Stage.Should().Be("need_requirement_intent");

        var prematureMcp1 = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact
            {
                ArtifactType = "RetrieveMemoryByChunksResponse",
                Value = HarnessJson.ParseJsonElement("{}")
            }
        });
        prematureMcp1.Success.Should().BeFalse(
            "harness must reject MCP submission before reaching the MCP stage");
        prematureMcp1.Stage.Should().Be("error");

        // MCP submission at stage 2 (need_retrieval_chunk_set) is also hard-stopped
        var r1 = StartSession("Design migration — premature MCP test 2");
        SubmitRequirementIntent(r1.SessionId);
        // Now at need_retrieval_chunk_set — MCP still not valid

        var prematureMcp2 = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r1.SessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact
            {
                ArtifactType = "RetrieveMemoryByChunksResponse",
                Value = HarnessJson.ParseJsonElement("{}")
            }
        });
        prematureMcp2.Success.Should().BeFalse(
            "harness must reject MCP result at need_retrieval_chunk_set — MCP is not valid until stage 4");
        prematureMcp2.Stage.Should().Be("error");
    }

    [Fact]
    public void SkillDrivenLoop_MustSubmitAfterEachStage()
    {
        // SKILL PROOF: Planning skill requires submit-step-result after every single stage
        var planningSkill = ReadRuleOrFail("00-harness-control-plane.mdc");
        planningSkill.Should().Contain("submit-step-result",
            "planning skill must require submit-step-result after each stage");
        planningSkill.Should().Contain("submit it back",
            "planning skill must instruct agent to submit the result back after each stage");
        planningSkill.ToLowerInvariant().Should().Contain("do-not-skip",
            "planning skill must prohibit batching or skipping stage submissions");

        // HARNESS PROOF: Each submit advances exactly one stage — cannot batch or skip
        var r0 = StartSession("Design the approach — submit-per-stage test");
        r0.Stage.Should().Be("need_requirement_intent");

        var r1 = SubmitRequirementIntent(r0.SessionId);
        r1.Success.Should().BeTrue();
        r1.Stage.Should().Be("need_retrieval_chunk_set",
            "each correct submit advances exactly one stage");

        var r2 = SubmitRetrievalChunkSet(r0.SessionId);
        r2.Success.Should().BeTrue();
        r2.Stage.Should().Be("need_retrieval_chunk_validation",
            "each correct submit advances exactly one stage — no batching");

        var r3 = SubmitChunkQualityReport(r0.SessionId);
        r3.Success.Should().BeTrue();
        r3.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks",
            "each correct submit advances exactly one stage");

        // Re-submitting an already-accepted stage is hard-stopped
        var resubmit = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentValidateChunkQuality,
            Artifact = new Artifact
            {
                ArtifactType = "ChunkQualityReport",
                Value = HarnessJson.ParseJsonElement(
                    @"{""isValid"":true,""has_core_task"":true,""has_constraint"":false,""has_risk"":false,""has_pattern"":false,""has_similar_case"":false,""errors"":[],""warnings"":[]}")
            }
        });
        resubmit.Success.Should().BeFalse(
            "re-submitting an already-accepted stage must be hard-stopped");
        resubmit.Stage.Should().Be("error");
    }

    [Fact]
    public void SkillDrivenLoop_StopsOnInvalidCanonicalArtifact()
    {
        // SKILL PROOF: Failure skill mandates hard-stop on any harness validation failure
        var failureSkill = ReadRuleOrFail("01-harness-failure.mdc");
        failureSkill.Should().Contain("HARD STOP",
            "failure skill must mandate HARD STOP on harness errors");
        failureSkill.Should().Contain("STOP IMMEDIATELY",
            "failure skill must require STOP IMMEDIATELY");
        failureSkill.Should().Contain("Harness Validation Failure",
            "failure skill must identify harness validation failure as a distinct failure type");
        failureSkill.ToLowerInvariant().Should().Contain("repair by guessing",
            "failure skill must explicitly prohibit repair by guessing");

        // HARNESS PROOF scenario 1: MCP response missing chunk_results — must hard-stop
        var r0 = StartSession("Stop on invalid — MCP retrieve test");
        SubmitRequirementIntent(r0.SessionId);
        SubmitRetrievalChunkSet(r0.SessionId);
        SubmitChunkQualityReport(r0.SessionId);

        var invalidRetrieve = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact
            {
                ArtifactType = "RetrieveMemoryByChunksResponse",
                Value = HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-1"" }")
            }
        });
        invalidRetrieve.Success.Should().BeFalse("missing chunk_results must hard-stop");
        invalidRetrieve.Stage.Should().Be("error");
        invalidRetrieve.Errors.Should().Contain(e => e.Contains("chunk_results"),
            "error message must identify which canonical field is missing");

        // HARNESS PROOF scenario 2: Execution plan with empty constraints — must hard-stop
        var r1 = StartSession("Stop on invalid — execution plan test");
        SubmitRequirementIntent(r1.SessionId);
        SubmitRetrievalChunkSet(r1.SessionId);
        SubmitChunkQualityReport(r1.SessionId);
        SubmitRetrieveMemoryByChunksResponse(r1.SessionId);
        SubmitMergeRetrievalResultsResponse(r1.SessionId);
        SubmitBuildMemoryContextPackResponse(r1.SessionId);

        var invalidPlan = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r1.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateExecutionPlan,
            Artifact = new Artifact
            {
                ArtifactType = "ExecutionPlan",
                Value = HarnessJson.ParseJsonElement(@"
                {
                    ""task_id"": ""task-1"",
                    ""task"": ""Add feature"",
                    ""scope"": ""UI only"",
                    ""constraints"": [],
                    ""forbidden_actions"": [""modify engine""],
                    ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
                    ""deliverables"": [""d""]
                }")
            }
        });
        invalidPlan.Success.Should().BeFalse("empty constraints must hard-stop");
        invalidPlan.Stage.Should().Be("error");
        invalidPlan.Errors.Should().Contain(e => e.Contains("constraints"),
            "error must identify the empty constraints field");
    }

    [Fact]
    public void SkillDrivenLoop_CompletesOnlyAfterAllCanonicalStagesAccepted()
    {
        // SKILL PROOF: Planning skill covers all 9 canonical stages and specifies completion behavior
        var planningSkill = ReadRuleOrFail("00-harness-control-plane.mdc");
        planningSkill.Should().Contain("need_requirement_intent");
        planningSkill.Should().Contain("need_retrieval_chunk_set");
        planningSkill.Should().Contain("need_retrieval_chunk_validation");
        planningSkill.Should().Contain("need_mcp_retrieve_memory_by_chunks");
        planningSkill.Should().Contain("need_mcp_merge_retrieval_results");
        planningSkill.Should().Contain("need_mcp_build_memory_context_pack");
        planningSkill.Should().Contain("need_execution_plan");
        planningSkill.Should().Contain("need_worker_execution_packet");
        planningSkill.Should().Contain("complete",
            "planning skill must include the complete stage");
        planningSkill.ToLowerInvariant().Should().Contain("what to present",
            "planning skill must specify what to present at completion");

        // HARNESS PROOF: complete is unreachable until all 8 prior stages are accepted
        var r0 = StartSession("Complete only after all stages — early exit test");
        SubmitRequirementIntent(r0.SessionId);

        var earlyComplete = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.Complete,
            Artifact = new Artifact
            {
                ArtifactType = "Complete",
                Value = HarnessJson.ParseJsonElement("{}")
            }
        });
        earlyComplete.Success.Should().BeFalse(
            "cannot reach complete after only requirement intent — all 8 stages required");
        earlyComplete.Stage.Should().Be("error");

        // Full happy path: all 8 stage submissions are required before complete
        var r1 = StartSession("Complete only after all stages — full path test");
        SubmitRequirementIntent(r1.SessionId).Stage.Should().Be("need_retrieval_chunk_set");
        SubmitRetrievalChunkSet(r1.SessionId).Stage.Should().Be("need_retrieval_chunk_validation");
        SubmitChunkQualityReport(r1.SessionId).Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");
        SubmitRetrieveMemoryByChunksResponse(r1.SessionId).Stage.Should().Be("need_mcp_merge_retrieval_results");
        SubmitMergeRetrievalResultsResponse(r1.SessionId).Stage.Should().Be("need_mcp_build_memory_context_pack");
        SubmitBuildMemoryContextPackResponse(r1.SessionId).Stage.Should().Be("need_execution_plan");
        SubmitExecutionPlan(r1.SessionId).Stage.Should().Be("need_worker_execution_packet");

        var final = SubmitWorkerExecutionPacket(r1.SessionId);
        final.Success.Should().BeTrue("all 8 stages accepted — harness must now complete");
        final.Stage.Should().Be("complete");
        final.CompletionArtifacts.Should().NotBeNull("completion must include both final artifacts");
        final.CompletionArtifacts!.ExecutionPlan.Should().NotBeNull();
        final.CompletionArtifacts.WorkerExecutionPacket.Should().NotBeNull();
    }

    // ==========================================
    // 5 Named Skill Strength Tests
    // ==========================================

    [Fact]
    public void ActivationSkill_IsSemanticNotLexicalOnly()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        content.Should().Contain("not lexical",
            "activation skill must explicitly state that activation is NOT lexical — semantic intent is required");
        content.Should().Contain("semantic",
            "activation skill must require semantic intent detection, not keyword matching");
        content.Should().Contain("meaning and context",
            "activation skill must instruct agent to infer from meaning and context, not literal words");
        content.Should().Contain("insufficient",
            "activation skill must state that lexical-only matching is insufficient");
        content.Should().Contain("Detecting the word",
            "activation skill must call out the specific failure mode of detecting literal words");
    }

    [Fact]
    public void ActivationSkill_IsGenericAgentOriented()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        content.ToLowerInvariant().Should().Contain("generic agent",
            "activation skill must explicitly state it applies to any generic agent");
        content.Should().Contain("Claude",
            "activation skill must name Claude as an example generic agent");
        content.Should().Contain("Cursor",
            "activation skill must name Cursor as an example generic agent");
        content.Should().Contain("any other planning-capable agent",
            "activation skill must state it is not product-specific");
        content.Should().Contain("same regardless",
            "activation skill must state activation rules are the same regardless of which agent is running");
    }

    [Fact]
    public void PlanningRule_IsOperationalRunbook_NotSoftGuidance()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");

        content.Should().Contain("ALWAYS",
            "planning rule must use ALWAYS imperatives — soft 'try to' guidance is insufficient");
        content.Should().Contain("MUST",
            "planning rule must use MUST — requirements must be unambiguous");
        content.Should().Contain("FORBIDDEN",
            "planning rule must use FORBIDDEN — prohibitions must be explicit, not implicit");
        content.Should().Contain("NEVER",
            "planning rule must use NEVER for hard prohibitions");
        content.Should().Contain("NEVER skip stages",
            "planning rule must explicitly forbid skipping stages");
        content.ToLowerInvariant().Should().Contain("do-not-skip",
            "planning rule must have a dedicated do-not-skip section — not just inline mentions");
        content.ToLowerInvariant().Should().Contain("what to present",
            "planning rule must define completion behavior — completion is a prescribed action, not ad hoc");
    }

    [Fact]
    public void FailureRule_RequiresHardStop()
    {
        var content = ReadRuleOrFail("01-harness-failure.mdc");

        content.Should().Contain("HARD STOP",
            "failure rule must mandate HARD STOP — not soft 'consider stopping' language");
        content.Should().Contain("STOP IMMEDIATELY",
            "failure rule must require STOP IMMEDIATELY on any harness error");
        content.Should().Contain("NEVER",
            "failure rule must have NEVER prohibitions for repair-by-guessing and free-form fallback");
        content.Should().Contain("FORBIDDEN",
            "failure rule must mark forbidden behaviors explicitly");
        content.ToLowerInvariant().Should().Contain("repair by guessing",
            "failure rule must name and prohibit repair by guessing");
        content.ToLowerInvariant().Should().Contain("hard-stop checklist",
            "failure rule must include a hard-stop checklist for agent to verify before acting");
        content.Should().Contain("Harness Validation Failure",
            "failure rule must name all failure types — Harness Validation Failure");
        content.Should().Contain("MCP Tool Call Failure",
            "failure rule must name all failure types — MCP Tool Call Failure");
    }

    [Fact]
    public void McpToolRule_RequiresExactToolAndPayloadRequest()
    {
        var content = ReadRuleOrFail("03-harness-mcp-tool-calling.mdc");

        content.Should().Contain("EXACTLY",
            "MCP skill must require EXACTLY the specified tool — no substitutions allowed");
        content.ToLowerInvariant().Should().Contain("payload.request",
            "MCP skill must require passing payload.request exactly as harness provides it");
        content.Should().Contain("retrieve_memory_by_chunks",
            "MCP skill must name the exact retrieve tool");
        content.Should().Contain("merge_retrieval_results",
            "MCP skill must name the exact merge tool");
        content.Should().Contain("build_memory_context_pack",
            "MCP skill must name the exact context pack tool");
        content.Should().Contain("RAW",
            "MCP skill must require submitting the RAW MCP response — no modification allowed");
        content.ToLowerInvariant().Should().Contain("no substitutions",
            "MCP skill must explicitly prohibit tool substitutions");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testSessionsRoot))
            Directory.Delete(_testSessionsRoot, true);
    }
}
