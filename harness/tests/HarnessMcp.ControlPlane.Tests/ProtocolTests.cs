using System.Text.Json;
using HarnessMcp.ControlPlane;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

public class ProtocolTests : IDisposable
{
    private readonly string _testSessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _stateMachine;

    public ProtocolTests()
    {
        _testSessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testSessionsRoot);
        _store = new SessionStore(_testSessionsRoot);
        _stateMachine = new HarnessStateMachine(_store, new ValidationOptions());
    }

    [Fact]
    public void StartSession_ReturnsRequirementIntentStep()
    {
        var request = new StartSessionRequest { RawTask = "Add year switching" };
        var response = _stateMachine.StartSession(request);

        response.Success.Should().BeTrue();
        response.Stage.Should().Be("need_requirement_intent");
        response.NextAction.Should().Be(HarnessActionName.AgentGenerateRequirementIntent);
    }

    [Fact]
    public void SubmitRequirementIntent_AdvancesToChunkSet()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Add feature" }).SessionId;

        var intent = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""add switching"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""low""
        }");

        var response = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = intent }
        });

        response.Success.Should().BeTrue();
        response.Stage.Should().Be("need_retrieval_chunk_set");
    }

    [Fact]
    public void SubmitChunkSet_AdvancesToChunkValidation()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Add feature" }).SessionId;
        
        _SubmitRequirementIntent(sessionId);

        var chunkSet = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""complexity"": ""low"",
            ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""This is a test chunk describing implementation approach for adding year switching."" }
            ]
        }");

        var response = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = chunkSet }
        });

        response.Stage.Should().Be("need_retrieval_chunk_validation");
    }

    [Fact]
    public void SubmitChunkValidation_AdvancesToMcpRetrieve()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Add feature" }).SessionId;
        _SubmitRequirementIntent(sessionId);
        _SubmitChunkSet(sessionId);

        var report = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""isValid"": true, ""has_core_task"": true, ""has_constraint"": true, ""has_risk"": false, ""has_pattern"": true, ""has_similar_case"": false, ""errors"": [], ""warnings"": [] }");

        var response = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentValidateChunkQuality,
            Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = report }
        });

        response.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");
    }

    [Fact]
    public void SubmitRetrieveMemoryByChunks_AdvancesToMerge()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Add feature" }).SessionId;
        _SubmitRequirementIntent(sessionId);
        _SubmitChunkSet(sessionId);
        _SubmitChunkValidation(sessionId);

        var retrieve = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
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

        var response = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = retrieve }
        });

        response.Stage.Should().Be("need_mcp_merge_retrieval_results");
    }

    [Fact]
    public void CancelSession_ReturnsStopWithError()
    {
        var startResponse = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Add feature" });
        
        var response = _stateMachine.CancelSession(startResponse.SessionId);

        response.Success.Should().BeTrue();
        response.NextAction.Should().Be(HarnessActionName.StopWithError);
    }

    [Fact]
    public void WrongAction_ReturnsError()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "Add feature" }).SessionId;

        var response = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = "wrong_action",
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement("{}") }
        });

        response.Success.Should().BeFalse();
        response.NextAction.Should().Be(HarnessActionName.StopWithError);
    }

    private void _SubmitRequirementIntent(string sessionId)
    {
        var intentJson = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""task_type"": ""ui-change"",
            ""goal"": ""add switching"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""low""
        }");
        
        _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = intentJson }
        });
    }

    private void _SubmitChunkSet(string sessionId)
    {
        var chunkSetJson = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""complexity"": ""low"",
            ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""Test chunk about adding year switching functionality to the codebase."" }
            ]
        }");

        _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = chunkSetJson }
        });
    }

    private void _SubmitChunkValidation(string sessionId)
    {
        var reportJson = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"{ ""isValid"": true, ""has_core_task"": true, ""has_constraint"": true, ""has_risk"": false, ""has_pattern"": true, ""has_similar_case"": false, ""errors"": [], ""warnings"": [] }");
        _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentValidateChunkQuality,
            Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = reportJson }
        });
    }

    private void _SubmitMcpRetrieve(string sessionId)
    {
        var retrieveJson = HarnessMcp.ControlPlane.HarnessJson.ParseJsonElement(@"
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
        _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = retrieveJson }
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_testSessionsRoot))
            Directory.Delete(_testSessionsRoot, true);
    }
}