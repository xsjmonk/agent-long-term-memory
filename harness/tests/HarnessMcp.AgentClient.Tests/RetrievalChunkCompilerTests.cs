using FluentAssertions;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.Contracts;
using Xunit;
using System.Linq;

namespace HarnessMcp.AgentClient.Tests;

public sealed class RetrievalChunkCompilerTests
{
    [Fact]
    public void generates_expected_chunk_types_for_mixed_requirements()
    {
        var intent = new RequirementIntent(
            SessionId: "s1",
            TaskId: "t1",
            RawTask: "raw",
            TaskType: "web-change",
            Domain: "ui",
            Module: "cards",
            Feature: "ajax-switch",
            Goal: "support year switching without engine changes",
            RequestedOperations: new[] { "ajax refresh", "no full reload" },
            HardConstraints: new[] { "engine logic must not change" },
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: new[] { "avoid placement inconsistency" },
            CandidateLayers: new[] { "ui", "api" },
            RetrievalFocuses: new[] { "placement" },
            Ambiguities: Array.Empty<string>(),
            Complexity: "low");

        var compiler = new RetrievalChunkCompiler(new ScopeInferenceService(), new ChunkTextNormalizer());
        var chunkSet = compiler.Compile(intent);

        chunkSet.Chunks.Select(c => c.ChunkType).ToArray().Should().Equal(new[]
        {
            ChunkType.CoreTask,
            ChunkType.Constraint,
            ChunkType.Risk,
            ChunkType.Pattern,
            ChunkType.Pattern
        });
    }

    [Fact]
    public void generates_similar_case_chunk_for_medium_high_complexity()
    {
        var intent = new RequirementIntent(
            SessionId: "s1",
            TaskId: "t1",
            RawTask: "raw",
            TaskType: "web-change",
            Domain: null,
            Module: null,
            Feature: "ajax-switch",
            Goal: "goal",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: Array.Empty<string>(),
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: new[] { "ui" },
            RetrievalFocuses: Array.Empty<string>(),
            Ambiguities: Array.Empty<string>(),
            Complexity: "medium");

        var compiler = new RetrievalChunkCompiler(new ScopeInferenceService(), new ChunkTextNormalizer());
        var chunkSet = compiler.Compile(intent);

        chunkSet.Chunks.Last().ChunkType.Should().Be(ChunkType.SimilarCase);
        chunkSet.Chunks.Last().SimilarCase.Should().NotBeNull();
    }

    [Fact]
    public void never_mixes_multiple_purposes_into_one_chunk_text()
    {
        var intent = new RequirementIntent(
            SessionId: "s1",
            TaskId: "t1",
            RawTask: "raw",
            TaskType: "web-change",
            Domain: "ui",
            Module: "cards",
            Feature: "ajax-switch",
            Goal: "goal",
            RequestedOperations: new[] { "ajax refresh" },
            HardConstraints: new[] { "do not change engine logic" },
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: new[] { "avoid placement inconsistency" },
            CandidateLayers: new[] { "ui" },
            RetrievalFocuses: new[] { "placement" },
            Ambiguities: new[] { "maybe clarify" },
            Complexity: "high");

        var compiler = new RetrievalChunkCompiler(new ScopeInferenceService(), new ChunkTextNormalizer());
        var chunkSet = compiler.Compile(intent);

        var forbiddenMarkers = new[]
        {
            "core_task|",
            "constraint|",
            "risk|",
            "pattern|",
            "similar_case|",
            "task_type:",
            "goal:",
            "ambiguities:"
        };

        foreach (var c in chunkSet.Chunks)
        {
            c.Text!.Should().NotBeNullOrWhiteSpace();
            foreach (var marker in forbiddenMarkers)
            {
                c.Text!.IndexOf(marker, StringComparison.OrdinalIgnoreCase).Should().Be(-1);
            }
        }

        // Ambiguities must be preserved structurally, not injected into retrieval text.
        var coreChunk = chunkSet.Chunks.First(c => c.ChunkType == ChunkType.CoreTask);
        coreChunk.Text!.IndexOf("maybe clarify", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
    }
}

