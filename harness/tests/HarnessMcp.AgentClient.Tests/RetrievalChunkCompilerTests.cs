using FluentAssertions;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.Contracts;
using Xunit;

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

        var markers = new[]
        {
            (ChunkType.CoreTask, "core_task|"),
            (ChunkType.Constraint, "constraint|"),
            (ChunkType.Risk, "risk|"),
            (ChunkType.Pattern, "pattern|"),
            (ChunkType.SimilarCase, "similar_case|")
        };

        foreach (var c in chunkSet.Chunks)
        {
            var expected = markers.Single(m => m.Item1 == c.ChunkType).Item2;
            c.Text!.StartsWith(expected, StringComparison.OrdinalIgnoreCase).Should().BeTrue();

            foreach (var m in markers)
            {
                if (m.Item2.Equals(expected, StringComparison.OrdinalIgnoreCase)) continue;
                c.Text!.IndexOf(m.Item2, StringComparison.OrdinalIgnoreCase).Should().Be(-1);
            }
        }
    }
}

