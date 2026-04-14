using FluentAssertions;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.Contracts;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class ChunkQualityGateTests
{
    [Fact]
    public void fails_when_hard_constraint_exists_but_no_constraint_chunk_was_emitted()
    {
        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "tt",
            Domain: null,
            Module: null,
            Feature: null,
            Goal: "g",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: new[] { "engine logic must not change" },
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: Array.Empty<string>(),
            RetrievalFocuses: Array.Empty<string>(),
            Ambiguities: Array.Empty<string>(),
            Complexity: "low");

        var scopes = new PlannedChunkScopes(
            Domain: null,
            Module: null,
            Features: Array.Empty<string>(),
            Layers: Array.Empty<string>(),
            Concerns: Array.Empty<string>(),
            Repos: Array.Empty<string>(),
            Services: Array.Empty<string>(),
            Symbols: Array.Empty<string>());

        var chunkSet = new RetrievalChunkSet(
            SessionId: intent.SessionId,
            TaskId: intent.TaskId,
            Complexity: intent.Complexity,
            Chunks: new[]
            {
                new RetrievalChunk(
                    ChunkId: "c1",
                    ChunkType: ChunkType.CoreTask,
                    Text: "core_task|task_type:tt|goal:g",
                    Scopes: scopes,
                    SimilarCase: null)
            },
            CoverageReport: new ChunkCoverageReport(
                HasCoreTask: true,
                HasConstraint: false,
                HasRisk: false,
                HasPattern: false,
                HasSimilarCase: false));

        var gate = new ChunkQualityGate();
        var report = gate.Validate(chunkSet, intent);
        report.IsValid.Should().BeFalse();
        report.Errors.Should().Contain(e => e.Contains("no constraint chunk"));
    }

    [Fact]
    public void fails_when_chunk_text_is_too_long()
    {
        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "tt",
            Domain: null,
            Module: null,
            Feature: null,
            Goal: "g",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: new[] { "hc" },
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: Array.Empty<string>(),
            RetrievalFocuses: Array.Empty<string>(),
            Ambiguities: Array.Empty<string>(),
            Complexity: "low");

        var scopes = new PlannedChunkScopes(null, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        var longText = "constraint|" + new string('a', ChunkQualityGate.MaxChunkTextChars + 50);

        var chunkSet = new RetrievalChunkSet(
            SessionId: intent.SessionId,
            TaskId: intent.TaskId,
            Complexity: intent.Complexity,
            Chunks: new[]
            {
                new RetrievalChunk("core", ChunkType.CoreTask, "core_task|task_type:tt|goal:g", scopes, null),
                new RetrievalChunk("con", ChunkType.Constraint, longText, scopes, null)
            },
            CoverageReport: new ChunkCoverageReport(
                HasCoreTask: true,
                HasConstraint: true,
                HasRisk: false,
                HasPattern: false,
                HasSimilarCase: false));

        var report = new ChunkQualityGate().Validate(chunkSet, intent);
        report.IsValid.Should().BeFalse();
        report.Errors.Should().Contain(e => e.Contains("text too long"));
    }

    [Fact]
    public void fails_when_chunk_purpose_mixing_is_detected()
    {
        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "tt",
            Domain: null,
            Module: null,
            Feature: null,
            Goal: "g",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: new[] { "hc" },
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: Array.Empty<string>(),
            RetrievalFocuses: Array.Empty<string>(),
            Ambiguities: Array.Empty<string>(),
            Complexity: "low");

        var scopes = new PlannedChunkScopes(null, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        var mixed = "constraint|hc and also risk|boom";

        var chunkSet = new RetrievalChunkSet(
            SessionId: intent.SessionId,
            TaskId: intent.TaskId,
            Complexity: intent.Complexity,
            Chunks: new[]
            {
                new RetrievalChunk("core", ChunkType.CoreTask, "core_task|task_type:tt|goal:g", scopes, null),
                new RetrievalChunk("con", ChunkType.Constraint, mixed, scopes, null)
            },
            CoverageReport: new ChunkCoverageReport(
                HasCoreTask: true,
                HasConstraint: true,
                HasRisk: false,
                HasPattern: false,
                HasSimilarCase: false));

        var report = new ChunkQualityGate().Validate(chunkSet, intent);
        report.IsValid.Should().BeFalse();
        report.Errors.Should().Contain(e => e.Contains("purity"));
    }
}

