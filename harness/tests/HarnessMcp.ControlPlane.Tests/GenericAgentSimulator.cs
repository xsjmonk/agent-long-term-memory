using System.Text.Json;
using HarnessMcp.ControlPlane;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Deterministic in-memory simulation of a generic agent following the skill-driven harness planning loop.
///
/// This helper is test-only. It is not a production component.
/// It simulates a generic agent that:
///   1. Uses a rule-based semantic classifier (mirroring 04-harness-skill-activation.mdc)
///      to determine whether planning intent is active
///   2. Calls start-session when planning intent is detected
///   3. Produces valid canonical artifacts for each harness stage
///   4. Calls submit-step-result after each stage
///   5. Continues until complete or error
///   6. Can be configured for negative testing (wrong stages, wrong artifacts)
/// </summary>
public class GenericAgentSimulator
{
    private readonly HarnessStateMachine _stateMachine;

    public GenericAgentSimulator(HarnessStateMachine stateMachine)
    {
        _stateMachine = stateMachine;
    }

    // ==========================================
    // Semantic Intent Classifier
    // Mirrors the rules in 04-harness-skill-activation.mdc
    // ==========================================

    /// <summary>
    /// Determines whether a user request has semantic planning intent.
    /// Mirrors the activation skill (04-harness-skill-activation.mdc).
    /// This is NOT keyword matching — it uses semantic categories.
    /// </summary>
    public static bool HasSemanticPlanningIntent(string userRequest)
    {
        var lower = userRequest.ToLowerInvariant();

        // --- Execution-only phrases: override planning signals ---
        // These match non-activation examples in 04-harness-skill-activation.mdc
        string[] executionOnlyPhrases = {
            "let's do it",
            "implement it now",
            "run the tests",
            "run the approved",
            "execute this accepted",
            "looks good, proceed",
            "looks good, go ahead",
            "the plan looks good",
            "rename this variable",
            "rename this",
            "fix the typo",
            "what does this",
            "explain how"
        };
        foreach (var phrase in executionOnlyPhrases)
            if (lower.Contains(phrase)) return false;

        // --- Plan mode is always planning intent ---
        if (lower.Contains("plan mode")) return true;

        // --- Semantic planning intent signals ---
        // Matches positive activation examples in 04-harness-skill-activation.mdc
        string[] planningSignals = {
            "approach",
            "design",
            "strategy",
            "plan the",
            "plan for",
            "how should we",
            "how to implement",
            "how to proceed",
            "before coding",
            "investigate",
            "refactor",
            "migration",
            "rollout",
            "another agent",
            "outline",
            "decompose",
            "architecture",
            "safest way",
            "best way to implement",
            "methodology",
            "debugging plan",
            "prepare",
            "staged"
        };
        foreach (var signal in planningSignals)
            if (lower.Contains(signal)) return true;

        return false;
    }

    // ==========================================
    // Full Happy-Path Loop
    // ==========================================

    /// <summary>
    /// Runs the complete harness planning loop for the given request.
    /// Produces valid canonical artifacts at each stage.
    /// Returns a SimulationResult indicating whether the loop completed.
    /// </summary>
    public SimulationResult RunFullLoop(string rawTask)
    {
        if (!HasSemanticPlanningIntent(rawTask))
            return SimulationResult.NotActivated(rawTask);

        var r0 = _stateMachine.StartSession(new StartSessionRequest { RawTask = rawTask });
        if (!r0.Success)
            return SimulationResult.Failed(r0, "start-session failed");

        var sessionId = r0.SessionId;
        var current = r0;

        while (current.Stage != "complete" && current.Stage != "error")
        {
            var artifact = BuildValidArtifact(current.NextAction);
            current = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
            {
                SessionId = sessionId,
                CompletedAction = current.NextAction,
                Artifact = artifact
            });
        }

        return current.Stage == "complete"
            ? SimulationResult.Completed(current)
            : SimulationResult.Failed(current, "harness returned error during loop");
    }

    // ==========================================
    // Targeted Stage Runners (for fine-grained tests)
    // ==========================================

    /// <summary>
    /// Starts a harness session for the given task.
    /// Does NOT check activation intent — call HasSemanticPlanningIntent first if needed.
    /// </summary>
    public StepResponse StartSession(string rawTask)
        => _stateMachine.StartSession(new StartSessionRequest { RawTask = rawTask });

    /// <summary>
    /// Submits the correct valid artifact for the given action.
    /// </summary>
    public StepResponse SubmitValidArtifact(string sessionId, string action)
    {
        var artifact = BuildValidArtifact(action);
        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = action,
            Artifact = artifact
        });
    }

    /// <summary>
    /// Submits the wrong action (not the one harness expects at the current stage).
    /// Used to prove harness hard-stops on wrong actions.
    /// </summary>
    public StepResponse SubmitWrongAction(string sessionId, string wrongAction, string artifactType = "RequirementIntent")
    {
        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = wrongAction,
            Artifact = new Artifact
            {
                ArtifactType = artifactType,
                Value = HarnessJson.ParseJsonElement("{}")
            }
        });
    }

    /// <summary>
    /// Submits an invalid artifact shape for the given action.
    /// Used to prove harness rejects malformed artifacts.
    /// </summary>
    public StepResponse SubmitInvalidArtifact(string sessionId, string action, string invalidJson)
    {
        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = action,
            Artifact = new Artifact
            {
                ArtifactType = GetArtifactTypeForAction(action),
                Value = HarnessJson.ParseJsonElement(invalidJson)
            }
        });
    }

    /// <summary>
    /// Advances the session through all stages up to (but not including) the specified target action.
    /// Useful for positioning the session at a specific stage for targeted failure testing.
    /// </summary>
    public StepResponse AdvanceTo(string sessionId, string targetAction)
    {
        var current = _stateMachine.GetNextStep(sessionId);
        while (current.NextAction != targetAction && current.Stage != "error" && current.Stage != "complete")
        {
            var artifact = BuildValidArtifact(current.NextAction);
            current = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
            {
                SessionId = sessionId,
                CompletedAction = current.NextAction,
                Artifact = artifact
            });
        }
        return current;
    }

    /// <summary>
    /// Uses get-next-step to re-sync after simulated context loss.
    /// </summary>
    public StepResponse GetNextStep(string sessionId)
        => _stateMachine.GetNextStep(sessionId);

    /// <summary>
    /// Uses get-session-status to inspect current state.
    /// </summary>
    public StepResponse GetSessionStatus(string sessionId)
        => _stateMachine.GetSessionStatus(sessionId);

    // ==========================================
    // Canonical Artifact Builders
    // ==========================================

    private static Artifact BuildValidArtifact(string action) => action switch
    {
        HarnessActionName.AgentGenerateRequirementIntent => BuildRequirementIntent(),
        HarnessActionName.AgentGenerateRetrievalChunkSet => BuildRetrievalChunkSet(),
        HarnessActionName.AgentValidateChunkQuality => BuildChunkQualityReport(),
        HarnessActionName.AgentCallMcpRetrieveMemoryByChunks => BuildRetrieveMemoryByChunksResponse(),
        HarnessActionName.AgentCallMcpMergeRetrievalResults => BuildMergeRetrievalResultsResponse(),
        HarnessActionName.AgentCallMcpBuildMemoryContextPack => BuildBuildMemoryContextPackResponse(),
        HarnessActionName.AgentGenerateExecutionPlan => BuildExecutionPlan(),
        HarnessActionName.AgentGenerateWorkerExecutionPacket => BuildWorkerExecutionPacket(),
        _ => throw new InvalidOperationException($"No valid artifact builder for action: {action}")
    };

    private static string GetArtifactTypeForAction(string action) => action switch
    {
        HarnessActionName.AgentGenerateRequirementIntent => "RequirementIntent",
        HarnessActionName.AgentGenerateRetrievalChunkSet => "RetrievalChunkSet",
        HarnessActionName.AgentValidateChunkQuality => "ChunkQualityReport",
        HarnessActionName.AgentCallMcpRetrieveMemoryByChunks => "RetrieveMemoryByChunksResponse",
        HarnessActionName.AgentCallMcpMergeRetrievalResults => "MergeRetrievalResultsResponse",
        HarnessActionName.AgentCallMcpBuildMemoryContextPack => "BuildMemoryContextPackResponse",
        HarnessActionName.AgentGenerateExecutionPlan => "ExecutionPlan",
        HarnessActionName.AgentGenerateWorkerExecutionPacket => "WorkerExecutionPacket",
        _ => "Unknown"
    };

    private static Artifact BuildRequirementIntent() => new()
    {
        ArtifactType = "RequirementIntent",
        Value = HarnessJson.ParseJsonElement(@"{
            ""task_id"": ""task-1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""implement new feature"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""low""
        }")
    };

    private static Artifact BuildRetrievalChunkSet() => new()
    {
        ArtifactType = "RetrievalChunkSet",
        Value = HarnessJson.ParseJsonElement(@"{
            ""task_id"": ""task-1"",
            ""complexity"": ""low"",
            ""chunks"": [{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""implement the feature"" }]
        }")
    };

    private static Artifact BuildChunkQualityReport() => new()
    {
        ArtifactType = "ChunkQualityReport",
        Value = HarnessJson.ParseJsonElement(@"{
            ""isValid"": true,
            ""has_core_task"": true,
            ""has_constraint"": false,
            ""has_risk"": false,
            ""has_pattern"": false,
            ""has_similar_case"": false,
            ""errors"": [],
            ""warnings"": []
        }")
    };

    private static Artifact BuildRetrieveMemoryByChunksResponse() => new()
    {
        ArtifactType = "RetrieveMemoryByChunksResponse",
        Value = HarnessJson.ParseJsonElement(@"{
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

    private static Artifact BuildMergeRetrievalResultsResponse() => new()
    {
        ArtifactType = "MergeRetrievalResultsResponse",
        Value = HarnessJson.ParseJsonElement(@"{
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
        }")
    };

    private static Artifact BuildBuildMemoryContextPackResponse() => new()
    {
        ArtifactType = "BuildMemoryContextPackResponse",
        Value = HarnessJson.ParseJsonElement(@"{
            ""task_id"": ""task-1"",
            ""memory_context_pack"": {
                ""must_follow"": [],
                ""best_practices"": [],
                ""avoid"": [],
                ""similar_case_guidance"": [],
                ""retrieval_support"": { ""multi_supported_items"": [], ""single_route_important_items"": [] }
            }
        }")
    };

    private static Artifact BuildExecutionPlan() => new()
    {
        ArtifactType = "ExecutionPlan",
        Value = HarnessJson.ParseJsonElement(@"{
            ""task_id"": ""task-1"",
            ""task"": ""Add feature to UI layer"",
            ""scope"": ""UI layer only"",
            ""constraints"": [""must not change engine""],
            ""forbidden_actions"": [""modify engine files"", ""change database schema""],
            ""steps"": [{
                ""step_number"": 1,
                ""title"": ""Create UI component"",
                ""actions"": [""Add new component file""],
                ""outputs"": [""Component file created""],
                ""acceptance_checks"": [""Component renders without errors""]
            }],
            ""deliverables"": [""New UI component""]
        }")
    };

    private static Artifact BuildWorkerExecutionPacket() => new()
    {
        ArtifactType = "WorkerExecutionPacket",
        Value = HarnessJson.ParseJsonElement(@"{
            ""goal"": ""Add feature to UI layer"",
            ""scope"": ""UI layer only"",
            ""hard_constraints"": [""must not change engine""],
            ""forbidden_actions"": [""modify engine files"", ""change database schema""],
            ""execution_rules"": [""Do NOT retrieve long-term memory independently. Do NOT replan. Do NOT expand scope.""],
            ""steps"": [{
                ""step_number"": 1,
                ""title"": ""Create UI component"",
                ""actions"": [""Add new component file""],
                ""outputs"": [""Component file created""],
                ""acceptance_checks"": [""Component renders without errors""]
            }],
            ""required_output_sections"": [""per_step_results"", ""final_deliverables"", ""validation_summary""]
        }")
    };
}

/// <summary>
/// Result of a simulated agent loop run.
/// </summary>
public class SimulationResult
{
    public bool WasActivated { get; private init; }
    public bool Succeeded { get; private init; }
    public string? FailureReason { get; private init; }
    public StepResponse? FinalResponse { get; private init; }
    public string RawTask { get; private init; } = string.Empty;

    public static SimulationResult NotActivated(string rawTask) => new()
    {
        WasActivated = false,
        Succeeded = false,
        FailureReason = "Planning intent not detected — activation skill did not trigger",
        RawTask = rawTask
    };

    public static SimulationResult Completed(StepResponse response) => new()
    {
        WasActivated = true,
        Succeeded = true,
        FinalResponse = response
    };

    public static SimulationResult Failed(StepResponse response, string reason) => new()
    {
        WasActivated = true,
        Succeeded = false,
        FailureReason = reason,
        FinalResponse = response
    };
}
