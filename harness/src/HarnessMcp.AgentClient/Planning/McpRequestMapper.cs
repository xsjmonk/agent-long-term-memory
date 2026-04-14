using HarnessMcp.Contracts;
using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Planning;

public sealed class McpRequestMapper
{
    public const bool DefaultActiveOnly = true;
    public const bool DefaultRequireTypeSeparation = true;
    public const bool DefaultIncludeEvidence = false;
    public const bool DefaultIncludeRawDetails = false;

    private static ScopeFilterDto EmptyScopeFilter() =>
        new(
            Domains: Array.Empty<string>(),
            Modules: Array.Empty<string>(),
            Features: Array.Empty<string>(),
            Layers: Array.Empty<string>(),
            Concerns: Array.Empty<string>(),
            Repos: Array.Empty<string>(),
            Services: Array.Empty<string>(),
            Symbols: Array.Empty<string>());

    private static ScopeFilterDto FromPlannedScopes(PlannedChunkScopes scopes)
    {
        return new ScopeFilterDto(
            Domains: scopes.Domain is null ? Array.Empty<string>() : new[] { scopes.Domain },
            Modules: scopes.Module is null ? Array.Empty<string>() : new[] { scopes.Module },
            Features: scopes.Features ?? Array.Empty<string>(),
            Layers: scopes.Layers ?? Array.Empty<string>(),
            Concerns: scopes.Concerns ?? Array.Empty<string>(),
            Repos: scopes.Repos ?? Array.Empty<string>(),
            Services: scopes.Services ?? Array.Empty<string>(),
            Symbols: scopes.Symbols ?? Array.Empty<string>());
    }

    public RetrieveMemoryByChunksRequest MapRetrieveMemoryByChunksRequest(
        RequirementIntent intent,
        RetrievalChunkSet chunkSet,
        string requestId,
        AuthorityLevel minimumAuthority,
        int maxItemsPerChunk,
        IReadOnlyList<string> chunkSearchDiagnostics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requirementIntentDto = new RequirementIntentDto(
            TaskType: intent.TaskType,
            Domain: intent.Domain,
            Module: intent.Module,
            Feature: intent.Feature,
            HardConstraints: intent.HardConstraints,
            RiskSignals: intent.RiskSignals);

        var retrievalChunks = chunkSet.Chunks.Select(c =>
        {
            SimilarCaseShapeDto? taskShape = c.SimilarCase is null
                ? null
                : new SimilarCaseShapeDto(
                    TaskType: c.SimilarCase.TaskType,
                    FeatureShape: c.SimilarCase.FeatureShape,
                    EngineChangeAllowed: c.SimilarCase.EngineChangeAllowed,
                    LikelyLayers: c.SimilarCase.LikelyLayers,
                    RiskSignals: c.SimilarCase.RiskSignals,
                    Complexity: c.SimilarCase.Complexity);

            return new RetrievalChunkDto(
                ChunkId: c.ChunkId,
                ChunkType: c.ChunkType,
                Text: c.Text,
                StructuredScopes: FromPlannedScopes(c.Scopes),
                TaskShape: taskShape);
        }).ToArray();

        var searchProfile = new ChunkSearchProfileDto(
            ActiveOnly: DefaultActiveOnly,
            MinimumAuthority: minimumAuthority,
            MaxItemsPerChunk: maxItemsPerChunk,
            RequireTypeSeparation: DefaultRequireTypeSeparation);

        return new RetrieveMemoryByChunksRequest(
            SchemaVersion: SchemaConstants.CurrentSchemaVersion,
            RequestId: requestId,
            TaskId: intent.TaskId,
            RequirementIntent: requirementIntentDto,
            RetrievalChunks: retrievalChunks,
            SearchProfile: searchProfile);
    }

    public MergeRetrievalResultsRequest MapMergeRetrievalResultsRequest(
        RetrievalChunkSet chunkSet,
        RetrieveMemoryByChunksResponse retrieved,
        string requestId)
    {
        return new MergeRetrievalResultsRequest(
            SchemaVersion: SchemaConstants.CurrentSchemaVersion,
            RequestId: requestId,
            TaskId: chunkSet.TaskId,
            Retrieved: retrieved);
    }

    public BuildMemoryContextPackRequest MapBuildMemoryContextPackRequest(
        RequirementIntent intent,
        RetrievalChunkSet chunkSet,
        RetrieveMemoryByChunksResponse retrieved,
        MergeRetrievalResultsResponse merged,
        string requestId)
    {
        var requirementIntentDto = new RequirementIntentDto(
            TaskType: intent.TaskType,
            Domain: intent.Domain,
            Module: intent.Module,
            Feature: intent.Feature,
            HardConstraints: intent.HardConstraints,
            RiskSignals: intent.RiskSignals);

        return new BuildMemoryContextPackRequest(
            SchemaVersion: SchemaConstants.CurrentSchemaVersion,
            RequestId: requestId,
            TaskId: chunkSet.TaskId,
            RequirementIntent: requirementIntentDto,
            Retrieved: retrieved,
            Merged: merged);
    }

    public SearchKnowledgeRequest MapFallbackSearchRequest(
        string requestId,
        string taskId,
        QueryKind kind,
        string queryText,
        PlannedChunkScopes scopes,
        AuthorityLevel minimumAuthority,
        int topK)
    {
        var retrievalClasses = kind switch
        {
            QueryKind.SimilarCase => new[] { RetrievalClass.SimilarCase },
            QueryKind.Constraint => new[] { RetrievalClass.Constraint },
            QueryKind.Risk => new[] { RetrievalClass.Antipattern },
            _ => Array.Empty<RetrievalClass>()
        };

        var scopesDto = new ScopeFilterDto(
            Domains: scopes.Domain is null ? Array.Empty<string>() : new[] { scopes.Domain },
            Modules: scopes.Module is null ? Array.Empty<string>() : new[] { scopes.Module },
            Features: scopes.Features ?? Array.Empty<string>(),
            Layers: scopes.Layers ?? Array.Empty<string>(),
            Concerns: scopes.Concerns ?? Array.Empty<string>(),
            Repos: scopes.Repos ?? Array.Empty<string>(),
            Services: scopes.Services ?? Array.Empty<string>(),
            Symbols: scopes.Symbols ?? Array.Empty<string>());

        return new SearchKnowledgeRequest(
            SchemaVersion: SchemaConstants.CurrentSchemaVersion,
            RequestId: requestId,
            QueryText: queryText,
            QueryKind: kind,
            Scopes: scopesDto,
            RetrievalClasses: retrievalClasses,
            MinimumAuthority: minimumAuthority,
            Status: KnowledgeStatus.Active,
            TopK: topK,
            IncludeEvidence: DefaultIncludeEvidence,
            IncludeRawDetails: DefaultIncludeRawDetails);
    }

    public GetKnowledgeItemRequest MapGetKnowledgeItemRequest(
        string requestId,
        Guid knowledgeItemId)
    {
        return new GetKnowledgeItemRequest(
            SchemaVersion: SchemaConstants.CurrentSchemaVersion,
            RequestId: requestId,
            KnowledgeItemId: knowledgeItemId,
            IncludeRelations: true,
            IncludeSegments: true,
            IncludeLabels: true,
            IncludeTags: true,
            IncludeScopes: true);
    }
}

