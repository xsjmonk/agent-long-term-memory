using FluentAssertions;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.AgentClient.Support;
using HarnessMcp.Contracts;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class McpRequestMapperTests
{
    [Fact]
    public void maps_planning_chunks_into_mcp_retrieve_memory_by_chunks_request()
    {
        var intent = new RequirementIntent(
            SessionId: "s",
            TaskId: "t",
            RawTask: "raw",
            TaskType: "TaskX",
            Domain: "ui",
            Module: "cards",
            Feature: "ajax-switch",
            Goal: "goal",
            RequestedOperations: Array.Empty<string>(),
            HardConstraints: new[] { "engine logic must not change" },
            SoftConstraints: Array.Empty<string>(),
            RiskSignals: new[] { "avoid placement inconsistency" },
            CandidateLayers: new[] { "ui" },
            RetrievalFocuses: new[] { "placement" },
            Ambiguities: Array.Empty<string>(),
            Complexity: "low");

        var scopes = new PlannedChunkScopes(
            Domain: "ui",
            Module: "cards",
            Features: new[] { "ajax-switch" },
            Layers: new[] { "ui" },
            Concerns: new[] { "engine logic must not change", "avoid placement inconsistency" },
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
                    ChunkId: "ch1",
                    ChunkType: ChunkType.CoreTask,
                    Text: "core_task|task_type:TaskX|goal:goal",
                    Scopes: scopes,
                    SimilarCase: null),
                new RetrievalChunk(
                    ChunkId: "ch2",
                    ChunkType: ChunkType.Constraint,
                    Text: "constraint|engine logic must not change",
                    Scopes: scopes,
                    SimilarCase: null)
            },
            CoverageReport: new ChunkCoverageReport(
                HasCoreTask: true,
                HasConstraint: true,
                HasRisk: true,
                HasPattern: false,
                HasSimilarCase: false));

        var mapper = new McpRequestMapper();

        var req = mapper.MapRetrieveMemoryByChunksRequest(
            intent,
            chunkSet,
            requestId: "req1",
            minimumAuthority: AuthorityLevel.Reviewed,
            maxItemsPerChunk: 7,
            chunkSearchDiagnostics: Array.Empty<string>(),
            cancellationToken: CancellationToken.None);

        req.SchemaVersion.Should().Be(SchemaConstants.CurrentSchemaVersion);
        req.RequestId.Should().Be("req1");
        req.TaskId.Should().Be("t");

        req.RequirementIntent.TaskType.Should().Be("TaskX");
        req.RequirementIntent.Domain.Should().Be("ui");
        req.RequirementIntent.Module.Should().Be("cards");
        req.RequirementIntent.Feature.Should().Be("ajax-switch");
        req.RequirementIntent.HardConstraints.Should().Equal(intent.HardConstraints);
        req.RequirementIntent.RiskSignals.Should().Equal(intent.RiskSignals);

        req.SearchProfile.ActiveOnly.Should().BeTrue();
        req.SearchProfile.MinimumAuthority.Should().Be(AuthorityLevel.Reviewed);
        req.SearchProfile.MaxItemsPerChunk.Should().Be(7);
        req.SearchProfile.RequireTypeSeparation.Should().BeTrue();

        req.RetrievalChunks.Should().HaveCount(2);
        req.RetrievalChunks[0].ChunkId.Should().Be("ch1");
        req.RetrievalChunks[0].ChunkType.Should().Be(ChunkType.CoreTask);
        req.RetrievalChunks[1].ChunkType.Should().Be(ChunkType.Constraint);

        req.RetrievalChunks[0].StructuredScopes!.Domains.Should().Contain("ui");
        req.RetrievalChunks[0].StructuredScopes!.Modules.Should().Contain("cards");
        req.RetrievalChunks[0].StructuredScopes!.Features.Should().Contain("ajax-switch");
        req.RetrievalChunks[0].StructuredScopes!.Layers.Should().Contain("ui");
        req.RetrievalChunks[0].StructuredScopes!.Concerns.Should().Contain("engine logic must not change");
    }
}

