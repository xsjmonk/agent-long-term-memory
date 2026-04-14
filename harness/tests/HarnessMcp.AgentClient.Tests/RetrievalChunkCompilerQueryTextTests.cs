using FluentAssertions;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.Contracts;
using Xunit;
using System.Linq;

namespace HarnessMcp.AgentClient.Tests;

public sealed class RetrievalChunkCompilerQueryTextTests
{
    [Fact]
    public void core_task_chunk_text_does_not_contain_pseudo_protocol_markers_and_does_not_inject_ambiguities()
    {
        var ambiguities = new[] { "maybe clarify", "unclear target" };

        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "ui-change",
            Domain: "ui",
            Module: null,
            Feature: null,
            Goal: "year switching for weighted card",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: new[] { "engine logic must not change" },
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: new[] { "ui" },
            RetrievalFocuses: new[] { "placement" },
            Ambiguities: ambiguities,
            Complexity: "low");

        var compiler = new RetrievalChunkCompiler(new ScopeInferenceService(), new ChunkTextNormalizer());
        var chunkSet = compiler.Compile(intent);

        var core = chunkSet.Chunks.Single(c => c.ChunkType == ChunkType.CoreTask);
        core.Text!.IndexOf("core_task|", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
        core.Text!.IndexOf("task_type:", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
        core.Text!.IndexOf("goal:", StringComparison.OrdinalIgnoreCase).Should().Be(-1);

        foreach (var amb in ambiguities)
            core.Text.IndexOf(amb, StringComparison.OrdinalIgnoreCase).Should().Be(-1);
    }

    [Fact]
    public void constraint_risk_and_pattern_chunk_texts_do_not_contain_pseudo_protocol_markers()
    {
        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "web-change",
            Domain: null,
            Module: "cards",
            Feature: "ajax-switch",
            Goal: "goal",
            RequestedOperations: new[] { "ajax refresh with explicit loading state and no full reload" },
            HardConstraints: new[] { "engine logic must not change" },
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: new[] { "avoid recurrence of placement inconsistency caused by ui inference" },
            CandidateLayers: new[] { "ui" },
            RetrievalFocuses: Array.Empty<string>(),
            Ambiguities: Array.Empty<string>(),
            Complexity: "low");

        var compiler = new RetrievalChunkCompiler(new ScopeInferenceService(), new ChunkTextNormalizer());
        var chunkSet = compiler.Compile(intent);

        var constraint = chunkSet.Chunks.Single(c => c.ChunkType == ChunkType.Constraint);
        var risk = chunkSet.Chunks.Single(c => c.ChunkType == ChunkType.Risk);
        var pattern = chunkSet.Chunks.Single(c => c.ChunkType == ChunkType.Pattern);

        foreach (var txt in new[] { constraint.Text, risk.Text, pattern.Text })
        {
            txt!.IndexOf("constraint|", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
            txt!.IndexOf("risk|", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
            txt!.IndexOf("pattern|", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
        }
    }

    [Fact]
    public void retrieval_focuses_are_not_copied_into_scope_layers()
    {
        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "ui-change",
            Domain: null,
            Module: null,
            Feature: null,
            Goal: "do it",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: Array.Empty<string>(),
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            CandidateLayers: new[] { "ui" },
            RetrievalFocuses: new[] { "placement" },
            Ambiguities: Array.Empty<string>(),
            Complexity: "low");

        var compiler = new RetrievalChunkCompiler(new ScopeInferenceService(), new ChunkTextNormalizer());
        var chunkSet = compiler.Compile(intent);

        foreach (var c in chunkSet.Chunks)
        {
            c.Scopes.Layers.Should().NotContain("placement");
        }
    }
}

