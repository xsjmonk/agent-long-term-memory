using System.Text.Json;
using FluentAssertions;
using HarnessMcp.ControlPlane;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

public sealed class AotJsonSerializationTests
{
    [Fact]
    public void RuntimeOptions_Load_Parses_Config_When_Reflection_Defaults_Are_Disabled()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"harness-aot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "appsettings.harness.json");

        var configJson = """
                          {
                            "sessionsRoot": ".harness/sessions-test",
                            "schemaVersion": "2.0",
                            "validation": {
                              "requireConstraintChunk": false,
                              "requireRiskChunk": false,
                              "maxChunks": 99,
                              "maxPlanSteps": 123,
                              "chunkTextMaxLength": 10
                            }
                          }
                          """;
        File.WriteAllText(configPath, configJson);

        RuntimeOptions.ClearCache();
        try
        {
            var options = RuntimeOptions.Load(configPath, envVars: null);

            options.SessionsRoot.Should().Be(".harness/sessions-test");
            options.SchemaVersion.Should().Be("2.0");
            options.Validation.RequireConstraintChunk.Should().BeFalse();
            options.Validation.RequireRiskChunk.Should().BeFalse();
            options.Validation.MaxChunks.Should().Be(99);
            options.Validation.MaxPlanSteps.Should().Be(123);
            options.Validation.ChunkTextMaxLength.Should().Be(10);
        }
        finally
        {
            RuntimeOptions.ClearCache();
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SessionStore_Save_And_Load_RoundTrips_JsonElement_Artifacts()
    {
        var sessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-aot-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionsRoot);

        var store = new SessionStore(sessionsRoot);
        var session = new Session
        {
            SessionId = "s1",
            TaskId = "t1",
            RawTask = "task",
            CurrentStage = HarnessStage.NeedRequirementIntent,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        session.AcceptedRequirementIntent = HarnessJson.ParseJsonElement(
            @"{ ""task_id"": ""t1"", ""task_type"": ""ui"", ""goal"": ""g"", ""hard_constraints"": [], ""risk_signals"": [], ""complexity"": ""low"" }");

        store.Save(session);

        var loaded = store.Load("s1");
        loaded.Should().NotBeNull();
        loaded!.AcceptedRequirementIntent.Should().NotBeNull();

        loaded.AcceptedRequirementIntent!.Value.ValueKind.Should().Be(JsonValueKind.Object);
        loaded.AcceptedRequirementIntent!.Value.TryGetProperty("task_id", out var taskId).Should().BeTrue();
        taskId.GetString().Should().Be("t1");
    }

    [Fact]
    public void DescribeProtocol_Serialization_Works_Without_Reflection()
    {
        var json = HarnessJson.SerializeProtocolDescription(new HarnessProtocolDescription());
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("schemaVersion", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("commands", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("stages", out _).Should().BeTrue();
    }

    [Fact]
    public void StartSession_Response_Serializes_Without_Reflection()
    {
        var sessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-aot-start-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionsRoot);
        var store = new SessionStore(sessionsRoot);
        var sm = new HarnessStateMachine(store, new ValidationOptions());

        var step = sm.StartSession(new StartSessionRequest { RawTask = "raw task text" });
        step.Success.Should().BeTrue();

        var json = HarnessJson.SerializeStepResponse(step);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("stage").GetString().Should().Be("need_requirement_intent");
        var payload = doc.RootElement.GetProperty("payload");
        payload.TryGetProperty("rawTask", out var rawTask).Should().BeTrue();
        rawTask.GetString().Should().Be("raw task text");
    }

    [Fact]
    public void SubmitStepResult_Artifact_Is_Parsed_As_JsonElement()
    {
        var el = HarnessJson.ParseJsonElement(@"{ ""foo"": ""bar"", ""n"": 1 }");
        el.ValueKind.Should().Be(JsonValueKind.Object);
        el.TryGetProperty("foo", out var foo).Should().BeTrue();
        foo.GetString().Should().Be("bar");
    }

    [Fact]
    public void GetNextStep_Response_With_Nested_Mcp_Request_Serializes_Without_Reflection()
    {
        var sessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-aot-mcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionsRoot);
        var store = new SessionStore(sessionsRoot);
        var sm = new HarnessStateMachine(store, new ValidationOptions());

        var start = sm.StartSession(new StartSessionRequest { RawTask = "task" });
        start.Success.Should().BeTrue();

        var requirementIntent = HarnessJson.ParseJsonElement(
            @"{ ""task_id"": ""task-1"", ""task_type"": ""ui"", ""goal"": ""g"", ""hard_constraints"": [""engine logic must not change""], ""risk_signals"": [], ""complexity"": ""low"" }");

        sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = start.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = requirementIntent }
        }).Success.Should().BeTrue();

        var retrievalChunkSet = HarnessJson.ParseJsonElement(
            @"{ ""task_id"": ""task-1"", ""complexity"": ""low"", ""chunks"": [
                { ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""year switching for yearly weighted card"" },
                { ""chunk_id"": ""c2"", ""chunk_type"": ""constraint"", ""text"": ""engine logic must not change"" }
             ] }");

        sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = start.SessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = retrievalChunkSet }
        }).Success.Should().BeTrue();

        var chunkQualityReport = HarnessJson.ParseJsonElement(
            @"{ ""isValid"": true, ""has_core_task"": true, ""has_constraint"": true, ""has_risk"": false, ""has_pattern"": false, ""has_similar_case"": false, ""errors"": [], ""warnings"": [] }");

        var afterQuality = sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = start.SessionId,
            CompletedAction = HarnessActionName.AgentValidateChunkQuality,
            Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = chunkQualityReport }
        });
        afterQuality.Success.Should().BeTrue();
        afterQuality.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");

        var json = HarnessJson.SerializeStepResponse(afterQuality);
        using var doc = JsonDocument.Parse(json);
        var payload = doc.RootElement.GetProperty("payload");
        payload.TryGetProperty("request", out var request).Should().BeTrue();
        request.ValueKind.Should().Be(JsonValueKind.Object);

        request.TryGetProperty("requirementIntent", out var reqIntent).Should().BeTrue();
        reqIntent.ValueKind.Should().Be(JsonValueKind.Object);

        request.TryGetProperty("retrievalChunks", out var retrievalChunks).Should().BeTrue();
        retrievalChunks.ValueKind.Should().Be(JsonValueKind.Object);
    }
}

