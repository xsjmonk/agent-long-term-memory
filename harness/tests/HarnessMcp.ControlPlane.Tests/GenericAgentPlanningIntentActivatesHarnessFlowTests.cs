using System.IO;
using System.Text.Json;
using FluentAssertions;
using HarnessMcp.ControlPlane;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Proves that semantic planning intent — "approach refactor", "debugging plan", "design rollout" —
/// correctly maps to harness activation and that the harness loop accepts these sessions.
///
/// Two-layer proof for each scenario:
///   1. Skill-content: the activation skill explicitly covers the scenario as a positive example.
///   2. Harness flow:  the harness starts and progresses through the full loop for these tasks.
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class GenericAgentPlanningIntentActivatesHarnessFlowTests : IDisposable
{
    private readonly string _sessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _sm;
    private const string ActivationSkillFile = "04-harness-skill-activation.mdc";

    public GenericAgentPlanningIntentActivatesHarnessFlowTests()
    {
        _sessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-planning-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sessionsRoot);
        _store = new SessionStore(_sessionsRoot);
        _sm = new HarnessStateMachine(_store, new ValidationOptions());
    }

    // ==========================================
    // Layer 1: Skill explicitly covers each scenario
    // ==========================================

    [Fact]
    public void Skill_ExplicitlyCovers_ApproachRefactor_AsPositiveActivation()
    {
        var content = ReadSkillOrFail();
        // "How should we approach this refactor?" — planning intent without the word "plan"
        content.Should().Contain("approach",
            "activation skill must list approach requests as positive activation signals");
        content.Should().Contain("refactor",
            "activation skill must explicitly name refactor as a positive example — canonical no-keyword planning-intent case");
    }

    [Fact]
    public void Skill_ExplicitlyCovers_DebuggingPlan_AsPositiveActivation()
    {
        var content = ReadSkillOrFail();
        // "Give me a debugging plan for this intermittent failure" → activate
        content.Should().Contain("debugging",
            "activation skill must cover debugging investigation as a positive activation scenario");
        content.Should().Contain("intermittent",
            "activation skill must explicitly contain the debugging-plan example with 'intermittent failure'");
    }

    [Fact]
    public void Skill_ExplicitlyCovers_RolloutMigration_AsPositiveActivation()
    {
        var content = ReadSkillOrFail();
        // "Design a rollout for this migration" → activate
        content.Should().Contain("rollout",
            "activation skill must include rollout planning as a positive example");
        content.Should().Contain("migration",
            "activation skill must include migration planning as a positive example");
    }

    [Fact]
    public void Skill_ExplicitlyCovers_RootCauseInvestigation_AsPositiveActivation()
    {
        var content = ReadSkillOrFail();
        // "Investigate the root cause of this production issue and tell me the approach" → activate
        content.Should().Contain("root cause",
            "activation skill must cover root-cause investigation as a positive activation scenario");
    }

    [Fact]
    public void Skill_ExplicitlyCovers_PreparingAnotherAgent_AsPositiveActivation()
    {
        var content = ReadSkillOrFail();
        // "Prepare another agent to implement the feature" → activate
        content.Should().Contain("another agent",
            "activation skill must cover 'prepare another agent' as a planning intent scenario");
    }

    // ==========================================
    // Layer 2: Harness accepts planning-intent sessions and advances correctly
    // ==========================================

    [Fact]
    public void Harness_AcceptsSession_ForApproachRefactorTask()
    {
        // "How should we approach this refactor?" maps to a planning session
        var r = _sm.StartSession(new StartSessionRequest { RawTask = "How should we approach this refactor?" });

        r.Success.Should().BeTrue("planning-intent task must start a harness session successfully");
        r.Stage.Should().Be("need_requirement_intent",
            "harness starts at need_requirement_intent for any planning task");
        r.NextAction.Should().Be(HarnessActionName.AgentGenerateRequirementIntent);
    }

    [Fact]
    public void Harness_AcceptsSession_ForDebuggingPlanTask()
    {
        var r = _sm.StartSession(new StartSessionRequest
        {
            RawTask = "Give me a debugging plan for this intermittent failure"
        });

        r.Success.Should().BeTrue();
        r.Stage.Should().Be("need_requirement_intent");
    }

    [Fact]
    public void Harness_AcceptsSession_ForMigrationRolloutTask()
    {
        var r = _sm.StartSession(new StartSessionRequest
        {
            RawTask = "Design a rollout for this migration"
        });

        r.Success.Should().BeTrue();
        r.Stage.Should().Be("need_requirement_intent");
    }

    [Fact]
    public void Harness_FullLoop_CompletesFor_ApproachDesignTask()
    {
        // Prove harness completes full 9-stage loop for a planning-intent task
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design the migration approach" });
        r0.Success.Should().BeTrue();

        var r1 = SubmitRequirementIntent(r0.SessionId);
        r1.Success.Should().BeTrue();
        r1.Stage.Should().Be("need_retrieval_chunk_set");

        var r2 = SubmitRetrievalChunkSet(r0.SessionId);
        r2.Success.Should().BeTrue();
        r2.Stage.Should().Be("need_retrieval_chunk_validation");

        var r3 = SubmitChunkQualityReport(r0.SessionId);
        r3.Success.Should().BeTrue();
        r3.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");
        r3.ToolName.Should().Be("retrieve_memory_by_chunks");

        var r4 = SubmitRetrieveMemoryByChunksResponse(r0.SessionId);
        r4.Success.Should().BeTrue();
        r4.Stage.Should().Be("need_mcp_merge_retrieval_results");

        var r5 = SubmitMergeRetrievalResultsResponse(r0.SessionId);
        r5.Success.Should().BeTrue();
        r5.Stage.Should().Be("need_mcp_build_memory_context_pack");

        var r6 = SubmitBuildMemoryContextPackResponse(r0.SessionId);
        r6.Success.Should().BeTrue();
        r6.Stage.Should().Be("need_execution_plan");

        var r7 = SubmitExecutionPlan(r0.SessionId);
        r7.Success.Should().BeTrue();
        r7.Stage.Should().Be("need_worker_execution_packet");

        var r8 = SubmitWorkerExecutionPacket(r0.SessionId);
        r8.Success.Should().BeTrue();
        r8.Stage.Should().Be("complete");
        r8.NextAction.Should().Be(HarnessActionName.Complete);
        r8.CompletionArtifacts.Should().NotBeNull();
        r8.CompletionArtifacts!.ExecutionPlan.Should().NotBeNull();
        r8.CompletionArtifacts.WorkerExecutionPacket.Should().NotBeNull();
    }

    [Fact]
    public void Harness_ReturnsFirstStage_ForAnyPlanningTask_SoAgentCanBegin()
    {
        // All planning tasks start at need_requirement_intent — harness controls the loop entry point
        var tasks = new[]
        {
            "How should we approach this refactor?",
            "Design a migration rollout plan",
            "Give me a debugging strategy for this intermittent failure",
            "What's the best approach for implementing this new feature?",
            "Help me think through how to restructure this service"
        };

        foreach (var task in tasks)
        {
            var r = _sm.StartSession(new StartSessionRequest { RawTask = task });
            r.Success.Should().BeTrue($"task '{task}' must start a harness session");
            r.Stage.Should().Be("need_requirement_intent",
                $"task '{task}' must begin at need_requirement_intent — harness controls the entry point");
            r.NextAction.Should().Be(HarnessActionName.AgentGenerateRequirementIntent);
        }
    }

    // --- Helpers ---

    private string ReadSkillOrFail()
    {
        var root = FindHarnessRoot() ?? throw new DirectoryNotFoundException("Could not locate harness root.");
        var path = Path.Combine(root, "agent-rules", ActivationSkillFile);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Activation skill not found at: {path}");
        return File.ReadAllText(path);
    }

    private StepResponse SubmitRequirementIntent(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""task_type"": ""design"",
            ""goal"": ""design the migration"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""low""
        }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = v }
        });
    }

    private StepResponse SubmitRetrievalChunkSet(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""complexity"": ""low"",
            ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""design the migration"" }
            ]
        }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = v }
        });
    }

    private StepResponse SubmitChunkQualityReport(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
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
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentValidateChunkQuality,
            Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = v }
        });
    }

    private StepResponse SubmitRetrieveMemoryByChunksResponse(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""chunk_results"": [
                {
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
                }
            ]
        }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = v }
        });
    }

    private StepResponse SubmitMergeRetrievalResultsResponse(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
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
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpMergeRetrievalResults,
            Artifact = new Artifact { ArtifactType = "MergeRetrievalResultsResponse", Value = v }
        });
    }

    private StepResponse SubmitBuildMemoryContextPackResponse(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""memory_context_pack"": {
                ""must_follow"": [],
                ""best_practices"": [],
                ""avoid"": [],
                ""similar_case_guidance"": [],
                ""retrieval_support"": { ""multi_supported_items"": [], ""single_route_important_items"": [] }
            }
        }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpBuildMemoryContextPack,
            Artifact = new Artifact { ArtifactType = "BuildMemoryContextPackResponse", Value = v }
        });
    }

    private StepResponse SubmitExecutionPlan(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""task"": ""Design the migration"",
            ""scope"": ""migration layer"",
            ""constraints"": [""must not disrupt production""],
            ""forbidden_actions"": [""drop tables"", ""modify live schemas without approval""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""Plan migration"", ""actions"": [""Draft plan""], ""outputs"": [""Plan doc""], ""acceptance_checks"": [""Reviewed""] }],
            ""deliverables"": [""Migration plan""]
        }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateExecutionPlan,
            Artifact = new Artifact { ArtifactType = "ExecutionPlan", Value = v }
        });
    }

    private StepResponse SubmitWorkerExecutionPacket(string sessionId)
    {
        var v = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""goal"": ""Design the migration"",
            ""scope"": ""migration layer"",
            ""hard_constraints"": [""must not disrupt production""],
            ""forbidden_actions"": [""drop tables"", ""modify live schemas without approval""],
            ""execution_rules"": [""Do NOT retrieve long-term memory independently. Do NOT replan. Do NOT expand scope.""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""Plan migration"", ""actions"": [""Draft plan""], ""outputs"": [""Plan doc""], ""acceptance_checks"": [""Reviewed""] }],
            ""required_output_sections"": [""per_step_results"", ""final_deliverables"", ""validation_summary""]
        }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateWorkerExecutionPacket,
            Artifact = new Artifact { ArtifactType = "WorkerExecutionPacket", Value = v }
        });
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
