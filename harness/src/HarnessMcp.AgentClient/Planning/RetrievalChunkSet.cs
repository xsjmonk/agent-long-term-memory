using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Planning;

public sealed record ChunkCoverageReport(
    bool HasCoreTask,
    bool HasConstraint,
    bool HasRisk,
    bool HasPattern,
    bool HasSimilarCase);

public sealed record RetrievalChunkSet(
    string SessionId,
    string TaskId,
    string Complexity,
    IReadOnlyList<RetrievalChunk> Chunks,
    ChunkCoverageReport CoverageReport);

public sealed record RetrievalChunk(
    string ChunkId,
    ChunkType ChunkType,
    string? Text,
    PlannedChunkScopes Scopes,
    SimilarCaseSignature? SimilarCase);

public sealed record PlannedChunkScopes(
    string? Domain,
    string? Module,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Layers,
    IReadOnlyList<string> Concerns,
    IReadOnlyList<string> Repos,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> Symbols);

public sealed record SimilarCaseSignature(
    string TaskType,
    string FeatureShape,
    bool EngineChangeAllowed,
    IReadOnlyList<string> LikelyLayers,
    IReadOnlyList<string> RiskSignals,
    string? Complexity);

