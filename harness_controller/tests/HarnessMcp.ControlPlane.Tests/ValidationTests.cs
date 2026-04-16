using System.Text.Json;
using HarnessMcp.ControlPlane;
using HarnessMcp.ControlPlane.Validators;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

public class ValidationTests : IDisposable
{
    private readonly string _testSessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _stateMachine;

    public ValidationTests()
    {
        _testSessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testSessionsRoot);
        _store = new SessionStore(_testSessionsRoot);
        _stateMachine = new HarnessStateMachine(_store, new ValidationOptions());
    }

    [Fact]
    public void InvalidRequirementIntent_ReturnsError()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "test" }).SessionId;

        var invalidIntent = HarnessJson.ParseJsonElement(@"{ ""task_id"": """" }");

        var response = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = invalidIntent }
        });

        response.Success.Should().BeFalse();
        response.Stage.Should().Be("error");
        response.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void WrongAction_ReturnsError()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "test" }).SessionId;

        var response = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = "agent_wrong_action",
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = HarnessJson.ParseJsonElement("{}") }
        });

        response.Success.Should().BeFalse();
        response.Errors.Should().Contain(e => e.Contains("Expected action"));
    }

    [Fact]
    public void InvalidChunkQualityReport_ReturnsError_WhenIsValidFalse()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "test" }).SessionId;
        
        _SubmitRequirementIntent(sessionId);
        _SubmitChunkSet(sessionId);

        var invalidReport = HarnessJson.ParseJsonElement(@"{ ""isValid"": false, ""errors"": [""quality issue""] }");

        var response = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentValidateChunkQuality,
            Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = invalidReport }
        });

        response.Success.Should().BeFalse();
        response.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void InvalidRetrieveResponse_ReturnsError()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "test" }).SessionId;
        _SubmitRequirementIntent(sessionId);
        _SubmitChunkSet(sessionId);
        _SubmitChunkValidation(sessionId);

        var invalidResponse = HarnessJson.ParseJsonElement(@"{ ""wrong_field"": ""value"" }");

        var response = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = invalidResponse }
        });

        response.Success.Should().BeFalse();
    }

    [Fact]
    public void InvalidMergeResponse_ReturnsError()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "test" }).SessionId;
        _SubmitRequirementIntent(sessionId);
        _SubmitChunkSet(sessionId);
        _SubmitChunkValidation(sessionId);
        _SubmitMcpRetrieve(sessionId);

        var invalidResponse = HarnessJson.ParseJsonElement(@"{ ""wrong_field"": ""value"" }");

        var response = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpMergeRetrievalResults,
            Artifact = new Artifact { ArtifactType = "MergeRetrievalResultsResponse", Value = invalidResponse }
        });

        response.Success.Should().BeFalse();
    }

    [Fact]
    public void InvalidContextPackResponse_ReturnsError()
    {
        var sessionId = _stateMachine.StartSession(new StartSessionRequest { RawTask = "test" }).SessionId;
        _SubmitRequirementIntent(sessionId);
        _SubmitChunkSet(sessionId);
        _SubmitChunkValidation(sessionId);
        _SubmitMcpRetrieve(sessionId);
        _SubmitMerge(sessionId);

        var invalidResponse = HarnessJson.ParseJsonElement(@"{ ""some_other"": ""value"" }");

        var response = _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpBuildMemoryContextPack,
            Artifact = new Artifact { ArtifactType = "BuildMemoryContextPackResponse", Value = invalidResponse }
        });

        response.Success.Should().BeFalse();
    }

    [Fact]
    public void RetrieveResponseWithResultsAlias_ReturnsError()
    {
        var v = new RetrieveMemoryByChunksResponseValidator();
        var invalid = HarnessJson.ParseJsonElement(@"{ ""results"": [] }");
        var result = v.Validate(invalid);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chunk_results"));
    }

    [Fact]
    public void MergeResponseWithMergedResultsAlias_ReturnsError()
    {
        var v = new MergeRetrievalResultsResponseValidator();
        var invalid = HarnessJson.ParseJsonElement(@"{ ""merged_results"": [] }");
        var result = v.Validate(invalid);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("merged_results"));
    }

    [Fact]
    public void ContextPackResponseWithWrongField_ReturnsError()
    {
        var v = new BuildMemoryContextPackResponseValidator();
        var invalid = HarnessJson.ParseJsonElement(@"{ ""context_pack"": {} }");
        var result = v.Validate(invalid);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("memory_context_pack"));
    }

    [Fact]
    public void ChunkSetValidatorRejectsPurityViolation()
    {
        var validator = new RetrievalChunkSetValidator(new ValidationOptions());
        var invalid = HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""complexity"": ""low"",
            ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""constraint"", ""text"": ""must not reload the page with ajax"" }
            ]
        }");
        var result = validator.Validate(invalid, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("pattern"));
    }

    [Fact]
    public void ChunkSetValidatorRejectsDuplicateChunkId()
    {
        var validator = new RetrievalChunkSetValidator(new ValidationOptions());
        var invalid = HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""complexity"": ""low"",
            ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""test"" },
                { ""chunk_id"": ""c1"", ""chunk_type"": ""constraint"", ""text"": ""test"" }
            ]
        }");
        var result = validator.Validate(invalid, null);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("duplicate"));
    }

    [Fact]
    public void RequirementIntentRejectsInvalidComplexity()
    {
        var validator = new RequirementIntentValidator();
        var invalid = HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task_type"": ""ui"",
            ""goal"": ""test"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""invalid""
        }");
        var result = validator.Validate(invalid);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("complexity"));
    }

    [Fact]
    public void RequirementIntentRejectsUnknownFields()
    {
        var validator = new RequirementIntentValidator();
        var invalid = HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""t1"",
            ""task_type"": ""ui"",
            ""goal"": ""test"",
            ""hard_constraints"": [],
            ""risk_signals"": [],
            ""complexity"": ""low"",
            ""unknown_field"": ""value""
        }");
        var result = validator.Validate(invalid);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("unknown"));
    }

    private void _SubmitRequirementIntent(string sessionId)
    {
        var intentJson = HarnessJson.ParseJsonElement(@"
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
        var chunkSetJson = HarnessJson.ParseJsonElement(@"
        {
            ""task_id"": ""task-1"",
            ""complexity"": ""low"",
            ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""test core task description"" }
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
        var reportJson = HarnessJson.ParseJsonElement(@"{ ""isValid"": true, ""has_core_task"": true, ""has_constraint"": false, ""has_risk"": false, ""has_pattern"": false, ""has_similar_case"": false }");
        _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentValidateChunkQuality,
            Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = reportJson }
        });
    }

    private void _SubmitMcpRetrieve(string sessionId)
    {
        var retrieveJson = HarnessJson.ParseJsonElement(@"
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

    private void _SubmitMerge(string sessionId)
    {
        var mergeJson = HarnessJson.ParseJsonElement(@"
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
        _stateMachine.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpMergeRetrievalResults,
            Artifact = new Artifact { ArtifactType = "MergeRetrievalResultsResponse", Value = mergeJson }
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_testSessionsRoot))
            Directory.Delete(_testSessionsRoot, true);
    }
}