namespace HarnessMcp.Contracts;

public sealed record ScopeFilterDto(
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Layers,
    IReadOnlyList<string> Concerns,
    IReadOnlyList<string> Repos,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> Symbols);

public sealed record SearchKnowledgeRequest(
    string SchemaVersion,
    string RequestId,
    string QueryText,
    QueryKind QueryKind,
    ScopeFilterDto Scopes,
    IReadOnlyList<RetrievalClass> RetrievalClasses,
    AuthorityLevel MinimumAuthority,
    KnowledgeStatus Status,
    int TopK,
    bool IncludeEvidence,
    bool IncludeRawDetails);

public sealed record EvidenceDto(
    Guid SourceArtifactId,
    string? SourcePath,
    IReadOnlyList<string> HeadingPath,
    string Snippet,
    int? StartLine,
    int? EndLine);

public sealed record KnowledgeCandidateDto(
    Guid KnowledgeItemId,
    RetrievalClass RetrievalClass,
    string Title,
    string Summary,
    string? Details,
    double SemanticScore,
    double LexicalScore,
    double ScopeScore,
    double AuthorityScore,
    double CaseShapeScore,
    double FinalScore,
    AuthorityLevel Authority,
    KnowledgeStatus Status,
    ScopeFilterDto Scopes,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Tags,
    IReadOnlyList<EvidenceDto> Evidence,
    IReadOnlyList<string> SupportedByChunks,
    IReadOnlyList<string> SupportedByQueryKinds);

public sealed record SearchKnowledgeDiagnosticsDto(
    int LexicalCandidateCount,
    int VectorCandidateCount,
    int MergedCandidateCount,
    int FinalCandidateCount,
    long ElapsedMs,
    string QueryEmbeddingModel,
    string EmbeddingRoleUsed);

public sealed record SearchKnowledgeResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    IReadOnlyList<KnowledgeCandidateDto> Candidates,
    SearchKnowledgeDiagnosticsDto Diagnostics);

public sealed record RetrievalChunkDto(
    string ChunkId,
    ChunkType ChunkType,
    string? Text,
    ScopeFilterDto? StructuredScopes,
    SimilarCaseShapeDto? TaskShape);

public sealed record SimilarCaseShapeDto(
    string TaskType,
    string FeatureShape,
    bool EngineChangeAllowed,
    IReadOnlyList<string> LikelyLayers,
    IReadOnlyList<string> RiskSignals,
    string? Complexity);

public sealed record RetrieveMemoryByChunksRequest(
    string SchemaVersion,
    string RequestId,
    string TaskId,
    RequirementIntentDto RequirementIntent,
    IReadOnlyList<RetrievalChunkDto> RetrievalChunks,
    ChunkSearchProfileDto SearchProfile);

public sealed record RequirementIntentDto(
    string TaskType,
    string? Domain,
    string? Module,
    string? Feature,
    IReadOnlyList<string> HardConstraints,
    IReadOnlyList<string> RiskSignals);

public sealed record ChunkSearchProfileDto(
    bool ActiveOnly,
    AuthorityLevel MinimumAuthority,
    int MaxItemsPerChunk,
    bool RequireTypeSeparation);

public sealed record ChunkBucketDto(
    IReadOnlyList<KnowledgeCandidateDto> Decisions,
    IReadOnlyList<KnowledgeCandidateDto> BestPractices,
    IReadOnlyList<KnowledgeCandidateDto> Antipatterns,
    IReadOnlyList<KnowledgeCandidateDto> SimilarCases,
    IReadOnlyList<KnowledgeCandidateDto> Constraints,
    IReadOnlyList<KnowledgeCandidateDto> References,
    IReadOnlyList<KnowledgeCandidateDto> Structures);

public sealed record ChunkRetrievalResultDto(
    string ChunkId,
    ChunkType ChunkType,
    ChunkBucketDto Results,
    SearchKnowledgeDiagnosticsDto Diagnostics);

public sealed record RetrieveMemoryByChunksResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    string TaskId,
    IReadOnlyList<ChunkRetrievalResultDto> ChunkResults,
    IReadOnlyList<string> Notes,
    long ElapsedMs);

public sealed record MergedKnowledgeItemDto(
    KnowledgeCandidateDto Item,
    IReadOnlyList<string> SupportedByChunkIds,
    IReadOnlyList<ChunkType> SupportedByChunkTypes,
    IReadOnlyList<string> MergeRationales);

public sealed record MergeRetrievalResultsRequest(
    string SchemaVersion,
    string RequestId,
    string TaskId,
    RetrieveMemoryByChunksResponse Retrieved);

public sealed record MergeRetrievalResultsResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    string TaskId,
    IReadOnlyList<MergedKnowledgeItemDto> Decisions,
    IReadOnlyList<MergedKnowledgeItemDto> Constraints,
    IReadOnlyList<MergedKnowledgeItemDto> BestPractices,
    IReadOnlyList<MergedKnowledgeItemDto> AntiPatterns,
    IReadOnlyList<MergedKnowledgeItemDto> SimilarCases,
    IReadOnlyList<MergedKnowledgeItemDto> References,
    IReadOnlyList<MergedKnowledgeItemDto> Structures,
    IReadOnlyList<string> Warnings,
    long ElapsedMs);

public sealed record BuildMemoryContextPackRequest(
    string SchemaVersion,
    string RequestId,
    string TaskId,
    RequirementIntentDto RequirementIntent,
    RetrieveMemoryByChunksResponse Retrieved,
    MergeRetrievalResultsResponse Merged);

public sealed record ContextPackSectionDto(
    IReadOnlyList<MergedKnowledgeItemDto> Decisions,
    IReadOnlyList<MergedKnowledgeItemDto> Constraints,
    IReadOnlyList<MergedKnowledgeItemDto> BestPractices,
    IReadOnlyList<MergedKnowledgeItemDto> AntiPatterns,
    IReadOnlyList<MergedKnowledgeItemDto> SimilarCases,
    IReadOnlyList<MergedKnowledgeItemDto> References,
    IReadOnlyList<MergedKnowledgeItemDto> Structures);

public sealed record ContextPackDiagnosticsDto(
    int ChunksProcessed,
    int DistinctKnowledgeItems,
    long RetrievalElapsedMs,
    long MergeElapsedMs,
    long AssemblyElapsedMs,
    IReadOnlyList<string> Warnings);

public sealed record BuildMemoryContextPackResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    string TaskId,
    ContextPackSectionDto ContextPack,
    ContextPackDiagnosticsDto Diagnostics);

public sealed record GetKnowledgeItemRequest(
    string SchemaVersion,
    string RequestId,
    Guid KnowledgeItemId,
    bool IncludeRelations,
    bool IncludeSegments,
    bool IncludeLabels,
    bool IncludeTags,
    bool IncludeScopes);

public sealed record KnowledgeSegmentDto(
    Guid SourceSegmentId,
    string SpanLevel,
    IReadOnlyList<string> HeadingPath,
    int? StartLine,
    int? EndLine,
    int? StartOffset,
    int? EndOffset,
    string Role,
    string? SourcePath);

public sealed record RelatedKnowledgeDto(
    Guid KnowledgeItemId,
    RelationType RelationType,
    string Title,
    string Summary,
    RetrievalClass RetrievalClass,
    AuthorityLevel Authority,
    double RelationStrength);

public sealed record GetKnowledgeItemResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    KnowledgeCandidateDto Item,
    IReadOnlyList<KnowledgeSegmentDto> Segments,
    IReadOnlyList<RelatedKnowledgeDto> Relations);

public sealed record GetRelatedKnowledgeRequest(
    string SchemaVersion,
    string RequestId,
    Guid KnowledgeItemId,
    IReadOnlyList<RelationType> RelationTypes,
    int TopK);

public sealed record GetRelatedKnowledgeResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    Guid KnowledgeItemId,
    IReadOnlyList<RelatedKnowledgeDto> Items);

public sealed record ServerInfoResponse(
    string SchemaVersion,
    string Kind,
    string ServerName,
    string ServerVersion,
    string ProtocolMode,
    FeatureFlagsDto Features,
    SchemaSetDto SchemaSet);

public sealed record FeatureFlagsDto(
    bool RetrieveMemoryByChunks,
    bool MergeRetrievalResults,
    bool BuildMemoryContextPack,
    bool SearchKnowledge,
    bool GetKnowledgeItem,
    bool GetRelatedKnowledge,
    bool HttpTransport,
    bool StdioTransport,
    bool WriteOperations,
    bool MonitoringUi,
    bool RealtimeTracking);

public sealed record SchemaSetDto(
    string RetrieveMemoryByChunks,
    string MergeRetrievalResults,
    string BuildMemoryContextPack,
    string SearchKnowledge,
    string GetKnowledgeItem,
    string GetRelatedKnowledge,
    string GetServerInfo);
