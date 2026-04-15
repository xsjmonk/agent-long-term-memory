using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class ChunkQueryPlanner : IChunkQueryPlanner
{
    public SearchKnowledgeRequest BuildSearchRequest(
        RetrieveMemoryByChunksRequest request,
        RetrievalChunkDto chunk,
        string requestIdSuffix)
    {
        var (kind, classes) = Map(chunk.ChunkType);
        var scopes = chunk.StructuredScopes ?? ScopeDtos.Empty;

        var queryText = chunk.Text ?? string.Empty;

        return new SearchKnowledgeRequest(
            request.SchemaVersion,
            $"{request.RequestId}:{requestIdSuffix}",
            queryText,
            kind,
            scopes,
            classes,
            request.SearchProfile.MinimumAuthority,
            KnowledgeStatus.Active,
            request.SearchProfile.MaxItemsPerChunk,
            IncludeEvidence: false,
            IncludeRawDetails: false);
    }

    private static (QueryKind Kind, List<RetrievalClass> Classes) Map(ChunkType type) => type switch
    {
        ChunkType.CoreTask => (QueryKind.CoreTask,
            [RetrievalClass.Decision, RetrievalClass.BestPractice, RetrievalClass.Constraint, RetrievalClass.Reference]),
        ChunkType.Constraint => (QueryKind.Constraint,
            [RetrievalClass.Constraint, RetrievalClass.Decision, RetrievalClass.Antipattern]),
        ChunkType.Risk => (QueryKind.Risk,
            [RetrievalClass.Antipattern, RetrievalClass.Decision, RetrievalClass.SimilarCase]),
        ChunkType.Pattern => (QueryKind.Pattern,
            [RetrievalClass.BestPractice, RetrievalClass.Decision, RetrievalClass.Reference]),
        ChunkType.SimilarCase => (QueryKind.SimilarCase,
            [RetrievalClass.SimilarCase, RetrievalClass.Decision, RetrievalClass.BestPractice]),
        _ => throw new ValidationException($"Unknown chunk type: {type}")
    };
}
