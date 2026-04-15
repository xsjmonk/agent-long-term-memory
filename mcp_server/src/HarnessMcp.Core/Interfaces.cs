using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public interface IChunkRetrievalService
{
    ValueTask<RetrieveMemoryByChunksResponse> RetrieveMemoryByChunksAsync(
        RetrieveMemoryByChunksRequest request,
        CancellationToken cancellationToken);
}

public interface IRetrievalMergeService
{
    ValueTask<MergeRetrievalResultsResponse> MergeRetrievalResultsAsync(
        MergeRetrievalResultsRequest request,
        CancellationToken cancellationToken);
}

public interface IMemoryContextPackService
{
    ValueTask<BuildMemoryContextPackResponse> BuildMemoryContextPackAsync(
        BuildMemoryContextPackRequest request,
        CancellationToken cancellationToken);
}

public interface IKnowledgeSearchService
{
    ValueTask<SearchKnowledgeResponse> SearchKnowledgeAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken);
}

public interface IKnowledgeReadService
{
    ValueTask<GetKnowledgeItemResponse> GetKnowledgeItemAsync(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken);
}

public interface IRelatedKnowledgeService
{
    ValueTask<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken);
}

public interface IQueryEmbeddingService
{
    ValueTask<QueryEmbeddingResult> EmbedAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken);
}

public interface IEmbeddingMetadataInspector
{
    ValueTask<StoredEmbeddingMetadata?> GetMetadataForRoleAsync(
        QueryKind queryKind,
        CancellationToken cancellationToken);
}

public interface IEmbeddingCompatibilityChecker
{
    EmbeddingCompatibilityResult Check(
        QueryEmbeddingResult query,
        StoredEmbeddingMetadata? stored,
        EmbeddingConfig config);
}

public interface IHybridRankingService
{
    IReadOnlyList<KnowledgeCandidateDto> Rank(
        IReadOnlyList<KnowledgeCandidateDto> lexical,
        IReadOnlyList<KnowledgeCandidateDto> semantic,
        SearchKnowledgeRequest request);
}

public interface IChunkQueryPlanner
{
    SearchKnowledgeRequest BuildSearchRequest(
        RetrieveMemoryByChunksRequest request,
        RetrievalChunkDto chunk,
        string requestIdSuffix);
}

public interface IRequestValidator
{
    void Validate(SearchKnowledgeRequest request);
    void Validate(RetrieveMemoryByChunksRequest request);
    void Validate(MergeRetrievalResultsRequest request);
    void Validate(BuildMemoryContextPackRequest request);
    void Validate(GetKnowledgeItemRequest request);
    void Validate(GetRelatedKnowledgeRequest request);
}

public interface IScopeNormalizer
{
    ScopeFilterDto Normalize(ScopeFilterDto scopes);
}

public interface IAuthorityPolicy
{
    bool IsAllowed(AuthorityLevel actual, AuthorityLevel required);
}

public interface ISupersessionPolicy
{
    bool IsVisible(KnowledgeStatus status, Guid? supersededBy);
}

public interface IContextPackAssembler
{
    BuildMemoryContextPackResponse Assemble(
        BuildMemoryContextPackRequest request,
        MergeRetrievalResultsResponse merged,
        long assemblyElapsedMs);
}

public interface IMonitorEventSink
{
    void Publish(MonitorEventDto evt);
}

public interface IMonitorEventExporter
{
    ValueTask<MonitorBatchDto> GetSinceAsync(long lastSequence, int maxCount, CancellationToken cancellationToken);
}

public interface IContextPackCache
{
    void Put(BuildMemoryContextPackResponse response);
    bool TryGet(string taskId, out BuildMemoryContextPackResponse? response);
}

public interface IAppInfoProvider
{
    ServerInfoResponse GetServerInfo();
}

public interface IHealthProbe
{
    ValueTask<HealthProbeResult> CheckAsync(CancellationToken cancellationToken);
}

public interface IMonitorEventBuffer
{
    IReadOnlyList<MonitorEventDto> Snapshot();
    long LastSequence { get; }
}

public interface IKnowledgeRepository
{
    ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchLexicalAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchSemanticAsync(
        SearchKnowledgeRequest request,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken);

    ValueTask<GetKnowledgeItemResponse> GetKnowledgeItemAsync(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken);

    ValueTask<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken);
}

public interface ICaseShapeScoreProvider
{
    double ComputeScore(Guid knowledgeItemId, SimilarCaseShapeDto? requestedShape);
}

public interface IMonitoringSnapshotService
{
    ValueTask<MonitorSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken);
}

public interface IMonitorEventBroadcaster
{
    ValueTask BroadcastAsync(MonitorEventDto evt, CancellationToken cancellationToken);
}

/// <summary>
/// MCP-owned internal request context store.
/// Used to correlate a search request with /embed-query envelope fields and SimilarCase structural ranking.
/// This is NOT a harness dependency - it is MCP internal state created from MCP request DTOs
/// and lives only during request processing (not persisted, not exposed publicly).
/// </summary>
public interface ISearchRequestContextStore
{
    void Set(string requestId, SearchRequestContext context);
    bool TryGet(string requestId, out SearchRequestContext? context);
    void Remove(string requestId);
}

/// <summary>
/// MCP-owned internal request context.
/// Created from MCP request DTOs, used only during request processing for /embed-query envelope construction.
/// </summary>
public sealed record SearchRequestContext(
    string? TaskId,
    string? ChunkId,
    string? ChunkType,
    string? RetrievalRoleHint,
    SimilarCaseShapeDto? TaskShape,
    ScopeFilterDto? StructuredScopes,
    string Purpose);
