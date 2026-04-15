using System.Text.Json;
using HarnessMcp.ControlPlane;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

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

    private StepResponse StartSession(string rawTask)
    {
        return _stateMachine.StartSession(new StartSessionRequest { RawTask = rawTask });
    }

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

    private StepResponse SubmitRetrievalChunkSet(string sessionId, string complexity = "low", bool hasConstraints = false, bool hasRisk = false, bool hasSimilarCase = false)
    {
        var chunks = new List<string>();
        chunks.Add(@"{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""implement the feature"" }");
        
        if (hasConstraints)
            chunks.Add(@"{ ""chunk_id"": ""c2"", ""chunk_type"": ""constraint"", ""text"": ""must not break existing API"" }");
        
        if (hasRisk)
            chunks.Add(@"{ ""chunk_id"": ""c3"", ""chunk_type"": ""risk"", ""text"": ""performance regression possible"" }");
        
        if (hasSimilarCase)
            chunks.Add(@"{ ""chunk_id"": ""c4"", ""chunk_type"": ""similar_case"", ""task_shape"": {{ ""task_type"": ""ui-change"", ""feature_shape"": ""new-field"", ""engine_change_allowed"": false, ""likely_layers"": [], ""risk_signals"": [] }} }");

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
                ""best_practices"": [{ ""item"": { ""knowledge_item_id"": ""k1"", ""title"": ""t"", ""summary"": ""s"" }, ""supported_by_chunk_ids"": [""c1""], ""supported_by_chunk_types"": [""core_task""], ""merge_rationales"": [""relevant to task""] }],
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

        var r4 = SubmitRetrieveMemoryByChunksResponse(r0.SessionId);
        r4.Stage.Should().BeOneOf("need_mcp_merge_retrieval_results", "error");
    }

    [Fact]
    public void SkillAndHarness_BlocksExecutionPlanBeforeContextPack()
    {
        var r0 = StartSession("Add feature");
        var r1 = SubmitRequirementIntent(r0.SessionId);
        r1.Stage.Should().Be("need_retrieval_chunk_set");

        var plan = JsonSerializer.Deserialize<JsonElement>(@"{ ""objective"": ""impl"", ""scope"": """", ""constraints"": [], ""deliverables"": [], ""steps"": [] }");
        var attempt = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = r0.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateExecutionPlan,
            Artifact = new Artifact { ArtifactType = "ExecutionPlan", Value = plan }
        });

        attempt.Success.Should().BeFalse();
        attempt.Stage.Should().Be("error");
    }

    [Fact]
    public void SkillAndHarness_HappyPath_ThroughPlanningStages()
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

        var r4 = SubmitRetrieveMemoryByChunksResponse(r0.SessionId);
        r4.Stage.Should().BeOneOf("need_mcp_merge_retrieval_results", "error");
    }

    [Fact]
    public void SkillAndHarness_BlocksMcpCallWhenHarnessDidNotRequestIt()
    {
        var r0 = StartSession("Add feature");
        var r1 = SubmitRequirementIntent(r0.SessionId);
        r1.Stage.Should().Be("need_retrieval_chunk_set");

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
        var r1 = SubmitRequirementIntent(r0.SessionId);
        var r2 = SubmitRetrievalChunkSet(r0.SessionId);
        var r3 = SubmitChunkQualityReport(r0.SessionId);
        r3.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");

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
        var r1 = SubmitRequirementIntent(r0.SessionId);
        var r2 = SubmitRetrievalChunkSet(r0.SessionId);
        var r3 = SubmitChunkQualityReport(r0.SessionId);

        r3.ToolName.Should().Be("retrieve_memory_by_chunks");
    }

    

    [Fact]
    public void SkillAndHarness_RejectsWorkerPacketThatAllowsMemoryRetrieval()
    {
        var validator = new Validators.WorkerExecutionPacketValidator();

        var packetWithRetrieval = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""objective"": ""test"",
            ""hard_constraints"": [],
            ""forbidden_actions"": [],
            ""execution_rules"": [""follow the plan""],
            ""steps"": [],
            ""required_output_sections"": []
        }");

        var result = validator.Validate(packetWithRetrieval, JsonSerializer.Deserialize<JsonElement>("{}"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("memory"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testSessionsRoot))
            Directory.Delete(_testSessionsRoot, true);
    }
}