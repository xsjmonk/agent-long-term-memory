using System.IO;
using System.Text.Json;
using FluentAssertions;
using HarnessMcp.ControlPlane;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Proves that the skill-driven harness loop enforces exact MCP tool names and
/// that agents cannot substitute, rename, or call unrequested MCP tools.
///
/// Two-layer proof:
///   1. Skill-content: MCP skill (03-harness-mcp-tool-calling.mdc) mandates EXACTLY the tool name
///      from payload.request, RAW response submission, and lists concrete INVALID vs CORRECT examples.
///   2. Harness behavior: each MCP stage returns the correct exact toolName + payload.request;
///      submitting the wrong MCP tool response is rejected.
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class SkillDrivenHarnessLoopCallsOnlyRequestedMcpToolTests : IDisposable
{
    private readonly string _sessionsRoot;
    private readonly SessionStore _store;
    private readonly HarnessStateMachine _sm;
    private const string McpSkillFile = "03-harness-mcp-tool-calling.mdc";

    public SkillDrivenHarnessLoopCallsOnlyRequestedMcpToolTests()
    {
        _sessionsRoot = Path.Combine(Path.GetTempPath(), $"harness-mcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sessionsRoot);
        _store = new SessionStore(_sessionsRoot);
        _sm = new HarnessStateMachine(_store, new ValidationOptions());
    }

    // ==========================================
    // Layer 1: MCP skill mandates exact tool names and behaviors
    // ==========================================

    [Fact]
    public void McpSkill_ContainsExactlyKeyword_ForToolNameUsage()
    {
        var content = ReadSkillOrFail();
        content.Should().Contain("EXACTLY",
            "MCP skill must say EXACTLY when describing tool name usage — no paraphrasing or renaming allowed");
    }

    [Fact]
    public void McpSkill_ContainsAllThreeExactToolNames()
    {
        var content = ReadSkillOrFail();
        content.Should().Contain("retrieve_memory_by_chunks",
            "MCP skill must contain exact tool name retrieve_memory_by_chunks");
        content.Should().Contain("merge_retrieval_results",
            "MCP skill must contain exact tool name merge_retrieval_results");
        content.Should().Contain("build_memory_context_pack",
            "MCP skill must contain exact tool name build_memory_context_pack");
    }

    [Fact]
    public void McpSkill_RequiresPayloadRequestPassThrough()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("payload.request",
            "MCP skill must require using payload.request exactly — no manual construction");
    }

    [Fact]
    public void McpSkill_RequiresRawResponseSubmission()
    {
        var content = ReadSkillOrFail();
        content.Should().Contain("RAW",
            "MCP skill must require submitting the RAW MCP response — no filtering or reformatting");
    }

    [Fact]
    public void McpSkill_ContainsNegativeExamples_WithInvalidMarker()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("negative examples",
            "MCP skill must contain a 'Negative Examples' section");
        content.Should().Contain("INVALID",
            "MCP skill negative examples must mark invalid behaviors with INVALID");
    }

    [Fact]
    public void McpSkill_ContainsPositiveExamples_WithCorrectMarker()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("positive examples",
            "MCP skill must contain a 'Positive Examples' section showing correct behaviors");
        content.Should().Contain("CORRECT",
            "MCP skill positive examples must mark correct behaviors with CORRECT");
    }

    [Fact]
    public void McpSkill_PositiveExample_ShowsExactToolNameUsage()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("correct exact tool",
            "MCP skill positive examples must demonstrate using the exact tool name");
    }

    [Fact]
    public void McpSkill_AppliesToGenericAgents_Claude_And_Cursor()
    {
        var content = ReadSkillOrFail();
        content.ToLowerInvariant().Should().Contain("generic agent",
            "MCP skill must state it applies to any generic agent");
        content.ToLowerInvariant().Should().Contain("claude",
            "MCP skill must name Claude as an example agent");
        content.ToLowerInvariant().Should().Contain("cursor",
            "MCP skill must name Cursor as an example agent");
    }

    // ==========================================
    // Layer 2: Harness returns exact tool names and rejects wrong MCP responses
    // ==========================================

    [Fact]
    public void Harness_Stage_RetrieveMemoryByChunks_ReturnsExactToolName()
    {
        var (_, r3) = AdvanceToMcpRetrieve();

        r3.ToolName.Should().Be("retrieve_memory_by_chunks",
            "harness must return EXACTLY 'retrieve_memory_by_chunks' — matches MCP skill's exact-tool-name mandate");
    }

    [Fact]
    public void Harness_Stage_RetrieveMemoryByChunks_HasPayloadRequest()
    {
        var (_, r3) = AdvanceToMcpRetrieve();

        r3.Payload.ValueKind.Should().Be(JsonValueKind.Object);
        r3.Payload.TryGetProperty("request", out _).Should().BeTrue();
    }

    [Fact]
    public void Harness_Stage_MergeRetrievalResults_ReturnsExactToolName()
    {
        var (sessionId, _) = AdvanceToMcpRetrieve();
        var r4 = SubmitRetrieveMemoryByChunksResponse(sessionId);

        r4.Stage.Should().Be("need_mcp_merge_retrieval_results");
        r4.ToolName.Should().Be("merge_retrieval_results",
            "harness must return EXACTLY 'merge_retrieval_results'");
        r4.Payload.ValueKind.Should().Be(JsonValueKind.Object);
        r4.Payload.TryGetProperty("request", out _).Should().BeTrue();
    }

    [Fact]
    public void Harness_Stage_BuildMemoryContextPack_ReturnsExactToolName()
    {
        var (sessionId, _) = AdvanceToMcpRetrieve();
        SubmitRetrieveMemoryByChunksResponse(sessionId);
        var r5 = SubmitMergeRetrievalResultsResponse(sessionId);

        r5.Stage.Should().Be("need_mcp_build_memory_context_pack");
        r5.ToolName.Should().Be("build_memory_context_pack",
            "harness must return EXACTLY 'build_memory_context_pack'");
        r5.Payload.ValueKind.Should().Be(JsonValueKind.Object);
        r5.Payload.TryGetProperty("request", out _).Should().BeTrue();
    }

    [Fact]
    public void Harness_Rejects_WrongMcpTool_AtRetrieveStage()
    {
        // At need_mcp_retrieve_memory_by_chunks, agent submits merge response instead
        var (sessionId, _) = AdvanceToMcpRetrieve();

        var attempt = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpMergeRetrievalResults, // WRONG
            Artifact = new Artifact { ArtifactType = "MergeRetrievalResultsResponse", Value = HarnessJson.ParseJsonElement("{}") }
        });

        attempt.Success.Should().BeFalse(
            "agent must not use a different MCP tool than the harness requested — wrong tool must be rejected");
        attempt.Stage.Should().Be("error");
        attempt.NextAction.Should().Be(HarnessActionName.StopWithError);
    }

    [Fact]
    public void Harness_Rejects_WrongMcpTool_AtMergeStage()
    {
        // At need_mcp_merge_retrieval_results, agent submits context pack response instead
        var (sessionId, _) = AdvanceToMcpRetrieve();
        SubmitRetrieveMemoryByChunksResponse(sessionId);

        var attempt = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpBuildMemoryContextPack, // WRONG
            Artifact = new Artifact { ArtifactType = "BuildMemoryContextPackResponse", Value = HarnessJson.ParseJsonElement("{}") }
        });

        attempt.Success.Should().BeFalse("wrong MCP tool at merge stage must be rejected");
        attempt.Stage.Should().Be("error");
    }

    [Fact]
    public void Harness_Rejects_WrongMcpTool_AtContextPackStage()
    {
        var (sessionId, _) = AdvanceToMcpRetrieve();
        SubmitRetrieveMemoryByChunksResponse(sessionId);
        SubmitMergeRetrievalResultsResponse(sessionId);

        var attempt = _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks, // WRONG — going backwards
            Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = HarnessJson.ParseJsonElement("{}") }
        });

        attempt.Success.Should().BeFalse("wrong MCP tool at context-pack stage must be rejected");
        attempt.Stage.Should().Be("error");
    }

    [Fact]
    public void Harness_AllThreeMcpStages_ReturnCorrectSequence()
    {
        var (sessionId, r3) = AdvanceToMcpRetrieve();

        // Stage 3: retrieve
        r3.Stage.Should().Be("need_mcp_retrieve_memory_by_chunks");
        r3.ToolName.Should().Be("retrieve_memory_by_chunks");
        r3.Payload.ValueKind.Should().Be(JsonValueKind.Object);
        r3.Payload.TryGetProperty("request", out _).Should().BeTrue();

        // Stage 4: merge
        var r4 = SubmitRetrieveMemoryByChunksResponse(sessionId);
        r4.Stage.Should().Be("need_mcp_merge_retrieval_results");
        r4.ToolName.Should().Be("merge_retrieval_results");
        r4.Payload.ValueKind.Should().Be(JsonValueKind.Object);
        r4.Payload.TryGetProperty("request", out _).Should().BeTrue();

        // Stage 5: context pack
        var r5 = SubmitMergeRetrievalResultsResponse(sessionId);
        r5.Stage.Should().Be("need_mcp_build_memory_context_pack");
        r5.ToolName.Should().Be("build_memory_context_pack");
        r5.Payload.ValueKind.Should().Be(JsonValueKind.Object);
        r5.Payload.TryGetProperty("request", out _).Should().BeTrue();
    }

    // --- Navigation helpers ---

    private (string sessionId, StepResponse r3) AdvanceToMcpRetrieve()
    {
        var r0 = _sm.StartSession(new StartSessionRequest { RawTask = "Design the feature" });
        SubmitRequirementIntent(r0.SessionId);
        SubmitRetrievalChunkSet(r0.SessionId);
        var r3 = SubmitChunkQualityReport(r0.SessionId);
        return (r0.SessionId, r3);
    }

    private StepResponse SubmitRequirementIntent(string sessionId)
    {
        var v = HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-1"", ""task_type"": ""ui-change"", ""goal"": ""implement feature"", ""hard_constraints"": [], ""risk_signals"": [], ""complexity"": ""low"" }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRequirementIntent,
            Artifact = new Artifact { ArtifactType = "RequirementIntent", Value = v }
        });
    }

    private StepResponse SubmitRetrievalChunkSet(string sessionId)
    {
        var v = HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-1"", ""complexity"": ""low"", ""chunks"": [{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""text"": ""implement feature"" }] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentGenerateRetrievalChunkSet,
            Artifact = new Artifact { ArtifactType = "RetrievalChunkSet", Value = v }
        });
    }

    private StepResponse SubmitChunkQualityReport(string sessionId)
    {
        var v = HarnessJson.ParseJsonElement(@"{ ""isValid"": true, ""has_core_task"": true, ""has_constraint"": false, ""has_risk"": false, ""has_pattern"": false, ""has_similar_case"": false, ""errors"": [], ""warnings"": [] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentValidateChunkQuality,
            Artifact = new Artifact { ArtifactType = "ChunkQualityReport", Value = v }
        });
    }

    private StepResponse SubmitRetrieveMemoryByChunksResponse(string sessionId)
    {
        var v = HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-1"", ""chunk_results"": [{ ""chunk_id"": ""c1"", ""chunk_type"": ""core_task"", ""results"": { ""decisions"": [], ""best_practices"": [{ ""knowledge_item_id"": ""k1"", ""title"": ""t"", ""summary"": ""s"" }], ""anti_patterns"": [], ""similar_cases"": [], ""constraints"": [], ""references"": [], ""structures"": [] } }] }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpRetrieveMemoryByChunks,
            Artifact = new Artifact { ArtifactType = "RetrieveMemoryByChunksResponse", Value = v }
        });
    }

    private StepResponse SubmitMergeRetrievalResultsResponse(string sessionId)
    {
        var v = HarnessJson.ParseJsonElement(@"{ ""task_id"": ""task-1"", ""merged"": { ""decisions"": [], ""constraints"": [], ""best_practices"": [{ ""item"": { ""knowledge_item_id"": ""k1"", ""title"": ""t"", ""summary"": ""s"" }, ""supported_by_chunk_ids"": [""c1""], ""supported_by_chunk_types"": [""core_task""], ""merge_rationales"": [""relevant""] }], ""anti_patterns"": [], ""similar_cases"": [], ""references"": [], ""structures"": [] } }");
        return _sm.SubmitStepResult(new SubmitStepResultRequest
        {
            SessionId = sessionId,
            CompletedAction = HarnessActionName.AgentCallMcpMergeRetrievalResults,
            Artifact = new Artifact { ArtifactType = "MergeRetrievalResultsResponse", Value = v }
        });
    }

    private string ReadSkillOrFail()
    {
        var root = FindHarnessRoot() ?? throw new DirectoryNotFoundException("Could not locate harness root.");
        var path = Path.Combine(root, ".cursor", "rules", McpSkillFile);
        if (!File.Exists(path))
            throw new FileNotFoundException($"MCP skill not found at: {path}");
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
