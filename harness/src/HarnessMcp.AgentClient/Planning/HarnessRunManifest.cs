namespace HarnessMcp.AgentClient.Planning;

public sealed record HarnessRunManifest(
    string ProtocolName,
    string ProtocolVersion,
    bool Success,
    string SessionId,
    string TaskId,
    string NextAction,
    string SessionJsonPath,
    string ExecutionPlanMarkdownPath,
    string WorkerPacketMarkdownPath,
    string? WorkerPacketText,
    bool UsedFallbackSearches,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

