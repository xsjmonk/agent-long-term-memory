namespace HarnessMcp.AgentClient.Planning;

public sealed record ChunkQualityReport(
    bool IsValid,
    bool HasCoreTask,
    bool HasConstraint,
    bool HasRisk,
    bool HasPattern,
    bool HasSimilarCase,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

