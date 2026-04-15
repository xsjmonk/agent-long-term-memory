using System.Text.Json;
using HarnessMcp.ControlPlane;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Integration-style tests that simulate a generic agent following skills through the harness loop.
/// These tests prove that skills + harness together control the flow and that invalid
/// behaviors are hard-stopped at every stage.
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class SkillAndHarnessFlowTests : IDisposable
{
    private readonly string _testSessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _stateMachine;

    public SkillAndHarnessFlowTests()
    {
        _testSessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-skill-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testSessionsRoot);
        _store = new SessionStore(_testSessionsRoot);
        _stateMachine = new HarnessStateMachine(_store, new ValidationOptions());
    }

    // --- Helper methods for building valid artifacts ---

    private StepResponse StartSession(string rawTask)
        => _stateMachine.StartSession(new StartSessionRequest { RawTask = rawTask });

    private StepResponse SubmitRequirementIntent(string sessionId, string complexity = "low",
        string[]? hardConstraints = null, string[]? riskSignals = null)
    {
        var hcJson = string.Join(",", (hardConstraints ?? Array.Empty<string>()).Select(c => $"\"{c}\""));
        var rsJson = string.Join(",", (riskSignals ?? Array.Empty<string>()).Select(r => $"\"{r}\""));
        var intent = JsonSerializer.Deserialize<JsonElement>($@"
        {{
            ""task_id"": ""task-1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""implement new feature"",
            ""hard_constraints"": [{hcJson}],
            ""risk_signals"": [{rsJson}],
            ""complexity"": ""{complexity}""
        }}");

        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = intent }
        });
    }

    private StepResponse SubmitRetrievalChunkSet(string sessionId, string complexity = "low",
        bool hasConstraints = false, bool hasRisk = false, bool hasSimilarCase = false)
    {
        var chunks = new List<string>();
        chunks.Add(@"{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""implement the feature"" }");

        if (hasConstraints)
            chunks.Add(@"{ ""chunk_id"": ""c2"", ""chunk_type"": ""constraint"", ""text"": ""must not break existing API"" }");

        if (hasRisk)
            chunks.Add(@"{ ""chunk_id"": ""c3"", ""chunk_type"": ""risk"", ""text"": ""performance regression possible"" }");

        if (hasSimilarCase)
            chunks.Add(@"{ ""chunk_id"": ""c4"", ""chunk_type"": ""similar_case"", ""task_shape"": { ""task_type"": ""ui-change"", ""feature_shape"": ""new-field"", ""engine_change_allowed"": false, ""likely_layers"": [], ""risk_signals"": [] } }");

        var chunkSet = JsonSerializer.Deserialize<JsonElement>($@"
        {{
            ""task_id"": ""task-1"",
            ""complexity"": ""{complexity}"",
            ""chunks"": [{string.Join(",", chunks)}]
        }}");

        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = chunkSet }
        });
    }

    private StepResponse SubmitChunkQualityReport(string sessionId, bool isValid = true)
    {
        var report = JsonSerializer.Deserialize<JsonElement>($@"
        {{
            ""isValid"": {isValid.ToString().ToLower()},
            ""has_core_task"": true,
            ""has_constraint"": false,
            ""has_risk"": false,
            ""has_pattern"": false,
            ""has_similar_case"": false,
            ""errors"": [],
            ""warnings"": []
        }}");

        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentValidateChunkQuality,
            Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = report }
        });
    }

    private StepResponse SubmitRetrieveMemoryByChunksResponse(string sessionId)
    {
        var response = JsonSerializer.Deserialize<JsonElement>(@"
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

        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = response }
        });
    }

    private StepResponse SubmitMergeRetrievalResultsResponse(string sessionId)
    {
        var response = JsonSerializer.Deserialize<JsonElement>(@"
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

        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpMergeRetrievalResults,
            Artifact = new Artifact { ArtifactType = "MergeRetrievalResultsResponse", Value = response }
        });
    }

    private StepResponse SubmitBuildMemoryContextPackResponse(string sessionId)
    {
        var response = JsonSerializer.Deserialize<JsonElement>(@"
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

        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpBuildMemoryContextPack,
            Artifact = new Artifact { ArtifactType = "BuildMemoryContextPackResponse", Value = response }
        });
    }

    private StepResponse SubmitExecutionPlan(string sessionId)
    {
        var plan = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""task_id"": ""task-1"",
            ""task"": ""Add feature to UI layer"",
            ""scope"": ""UI layer only"",
            ""constraints"": [""must not change engine""],
            ""forbidden_actions"": [""modify engine files"", ""change database schema""],
            ""steps"": [
                {
                    ""step_number"": 1,
                    ""title"": ""Create UI component"",
                    ""actions"": [""Add new component file""],
                    ""outputs"": [""Component file created""],
                    ""acceptance_checks"": [""Component renders without errors""]
                }
            ],
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
        var packet = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""goal"": ""Add feature to UI layer"",
            ""scope"": ""UI layer only"",
            ""hard_constraints"": [""must not change engine""],
            ""forbidden_actions"": [""modify engine files"", ""change database schema""],
            ""execution_rules"": [""Do NOT retrieve long-term memory independently. Do NOT replan. Do NOT expand scope.""],
            ""steps"": [
                {
                    ""step_number"": 1,
                    ""title"": ""Create UI component"",
                    ""actions"": [""Add new component file""],
                    ""outputs"": [""Component file created""],
                    ""acceptance_checks"": [""Component renders without errors""]
                }
            ],
            ""required_output_sections"": [""per_step_results"", ""final_deliverables"", ""validation_summary""]
        }");

        return _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateWorkerExecutionPacket,
            Artifact = new Artifact { ArtifactType = "WorkerExecutionPacket", Value = packet }
        });
    }

    // --- Full loop tests ---

    [Fact]
    public void SkillAndHarness_FullHappyPath_CompletesAllStages()
    {
        // Activation skill fires → planning skill activates → agent follows harness loop
        var r0 = StartSession("Add UI feature — non-trivial design task");
        r0.Success.Should().BeTrue();
        r0.Stage.Should().Be("need_requirement_intent");

        var r1 = SubmitRequirementIntent(r0.SessionId, hardConstraints: new[] { "must not change engine" });
        r1.Success.Should().BeTrue();
        r1.Stage.Should().Be("need_retrieval_chunk_set");

        var r2 = SubmitRetrievalChunkSet(r0.SessionId, hasConstraints: true);
        r2.Success.Should().BeTrue();
        r2.Stage.Should().Be("need_retrieval_chunk_validation");

        var r3 = SubmitChunkQualityReport(r0.SessionId);
        r3.Success.Should().BeTrue();
        r3.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");
        r3.ToolName.Should().Be("retrieve_memory_by_chunks");
        r3.Payload.Should().ContainKey("request");

        var r4 = SubmitRetrieveMemoryByChunksResponse(r0.SessionId);
        r4.Success.Should().BeTrue();
        r4.Stage.Should().Be("need_mcp_merge_retrieval_results");
        r4.ToolName.Should().Be("merge_retrieval_results");

        var r5 = SubmitMergeRetrievalResultsResponse(r0.SessionId);
        r5.Success.Should().BeTrue();
        r5.Stage.Should().Be("need_mcp_build_memory_context_pack");
        r5.ToolName.Should().Be("build_memory_context_pack");

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
    public void SkillAndHarness_HappyPath_UsesOnlyHarnessDirectedActions()
    {
        var r0 = StartSession("Add feature");
        r0.SessionId.Should().NotBeNullOrEmpty();
        r0.Stage.Should().Be("need_requirement_intent");

        var r1 = SubmitRequirementIntent(r0.SessionId);
        r1.Success.Should().BeTrue();
        r1.Stage.Should().Be("need_retrieval_chunk_set");

        var r2 = SubmitRetrievalChunkSet(r0.SessionId);
        r2.Success.Should().BeTrue();
        r2.Stage.Should().Be("need_retrieval_chunk_validation");

        var r3 = SubmitChunkQualityReport(r0.SessionId);
        r3.Success.Should().BeTrue();
        r3.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");
    }

    [Fact]
    public void SkillAndHarness_BlocksExecutionPlanBeforeContextPack()
    {
        var r0 = StartSession("Add feature");
        SubmitRequirementIntent(r0.SessionId);
        // Session is at need_retrieval_chunk_set — trying to submit ExecutionPlan must fail

        var wrongAction = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateExecutionPlan,
            Artifact = new Artifact { ArtifactType = "ExecutionPlan", Value = JsonSerializer.Deserialize<JsonElement>("{}") }
        });

        wrongAction.Success.Should().BeFalse();
        wrongAction.Stage.Should().Be("error");
    }

    [Fact]
    public void SkillAndHarness_BlocksMcpCallWhenHarnessDidNotRequestIt()
    {
        var r0 = StartSession("Add feature");
        var r1 = SubmitRequirementIntent(r0.SessionId);
        r1.Stage.Should().Be("need_retrieval_chunk_set");

        // Agent tries to call MCP before harness instructs it
        var attempt = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = JsonSerializer.Deserialize<JsonElement>("{}") }
        });

        attempt.Success.Should().BeFalse();
        attempt.Stage.Should().Be("error");
    }

    [Fact]
    public void SkillAndHarness_BlocksWrongMcpToolResultShape()
    {
        var r0 = StartSession("Add feature");
        SubmitRequirementIntent(r0.SessionId);
        SubmitRetrievalChunkSet(r0.SessionId);
        var r3 = SubmitChunkQualityReport(r0.SessionId);
        r3.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");

        // Agent submits wrong shape — harness must reject
        var wrongShape = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""task_id"": ""task-1"",
            ""results"": []
        }");

        var attempt = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = wrongShape }
        });

        attempt.Success.Should().BeFalse();
        attempt.Stage.Should().Be("error");
        attempt.Errors.Should().Contain(e => e.Contains("chunk_results"));
    }

    [Fact]
    public void SkillAndHarness_RejectsNonCanonicalChunkSet_MissingConstraint()
    {
        var r0 = StartSession("Add feature");
        var r1 = SubmitRequirementIntent(r0.SessionId, hardConstraints: new[] { "must-not-break" });
        r1.Stage.Should().Be("need_retrieval_chunk_set");

        // No constraint chunk despite hard_constraints being non-empty
        var r2 = SubmitRetrievalChunkSet(r0.SessionId, hasConstraints: false);
        r2.Success.Should().BeFalse();
        r2.Errors.Should().Contain(e => e.Contains("constraint"));
    }

    [Fact]
    public void SkillAndHarness_RejectsNonCanonicalChunkSet_MissingRisk()
    {
        var r0 = StartSession("Add feature");
        var r1 = SubmitRequirementIntent(r0.SessionId, riskSignals: new[] { "performance-issue" });
        r1.Stage.Should().Be("need_retrieval_chunk_set");

        var r2 = SubmitRetrievalChunkSet(r0.SessionId, hasRisk: false);
        r2.Success.Should().BeFalse();
        r2.Errors.Should().Contain(e => e.Contains("risk"));
    }

    [Fact]
    public void SkillAndHarness_RejectsNonCanonicalChunkSet_MissingSimilarCase()
    {
        var r0 = StartSession("Add feature");
        var r1 = SubmitRequirementIntent(r0.SessionId, complexity: "high");
        r1.Stage.Should().Be("need_retrieval_chunk_set");

        var r2 = SubmitRetrievalChunkSet(r0.SessionId, complexity: "high", hasSimilarCase: false);
        r2.Success.Should().BeFalse();
        r2.Errors.Should().Contain(e => e.Contains("similar_case"));
    }

    [Fact]
    public void SkillAndHarness_UsesExactMcpToolNamesFromHarness()
    {
        var r0 = StartSession("Add feature");
        SubmitRequirementIntent(r0.SessionId);
        SubmitRetrievalChunkSet(r0.SessionId);
        var r3 = SubmitChunkQualityReport(r0.SessionId);

        r3.ToolName.Should().Be("retrieve_memory_by_chunks");
        r3.Payload.Should().ContainKey("request");
    }

    [Fact]
    public void SkillAndHarness_RejectsWorkerPacketThatAllowsMemoryRetrieval()
    {
        var validator = new Validators.WorkerExecutionPacketValidator();

        // Packet has no memory prohibition in execution_rules — must fail
        var packetWithoutMemoryProhibition = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""goal"": ""test"",
            ""scope"": ""ui"",
            ""hard_constraints"": [""c1""],
            ""forbidden_actions"": [""f1""],
            ""execution_rules"": [""follow the plan""],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""required_output_sections"": [""per_step_results""]
        }");

        var result = validator.Validate(packetWithoutMemoryProhibition, JsonSerializer.Deserialize<JsonElement>("{}"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("memory"));
    }

    [Fact]
    public void SkillAndHarness_RejectsExecutionPlan_MissingCanonicalFields()
    {
        var validator = new Validators.ExecutionPlanValidator(new ValidationOptions());

        // Plan uses old 'objective' field instead of canonical 'task_id' + 'task'
        var nonCanonicalPlan = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""objective"": ""Add feature"",
            ""scope"": ""UI"",
            ""constraints"": [],
            ""steps"": [{ ""step_number"": 1, ""title"": ""s"", ""actions"": [""a""], ""outputs"": [""o""], ""acceptance_checks"": [""c""] }],
            ""deliverables"": [""d""]
        }");

        var result = validator.Validate(nonCanonicalPlan, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("task_id") || e.Contains("task"));
    }

    [Fact]
    public void SkillAndHarness_AcceptsExecutionPlan_WithAllCanonicalFields()
    {
        var validator = new Validators.ExecutionPlanValidator(new ValidationOptions());

        var canonicalPlan = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""task_id"": ""task-1"",
            ""task"": ""Add UI feature"",
            ""scope"": ""UI layer only"",
            ""constraints"": [],
            ""forbidden_actions"": [],
            ""steps"": [
                {
                    ""step_number"": 1,
                    ""title"": ""Create component"",
                    ""actions"": [""Write component""],
                    ""outputs"": [""Component.tsx""],
                    ""acceptance_checks"": [""Renders without error""]
                }
            ],
            ""deliverables"": [""Component.tsx""]
        }");

        var result = validator.Validate(canonicalPlan, null);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void SkillAndHarness_FlowCannotBypassHarness_CompletionRequiresAllStages()
    {
        // Verify that harness does not complete without all stages
        var r0 = StartSession("Design the migration");
        r0.Stage.Should().Be("need_requirement_intent");
        r0.NextAction.Should().Be(HarnessActionName.AgentGenerateRequirementIntent);

        // Trying to jump directly to complete — harness must reject
        var attempt = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.Complete,
            Artifact = new Artifact { ArtifactType = "Complete", Value = JsonSerializer.Deserialize<JsonElement>("{}") }
        });

        attempt.Success.Should().BeFalse();
        attempt.Stage.Should().Be("error");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testSessionsRoot))
            Directory.Delete(_testSessionsRoot, true);
    }
}
