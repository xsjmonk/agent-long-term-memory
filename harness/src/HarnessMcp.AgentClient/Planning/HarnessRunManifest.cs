namespace HarnessMcp.AgentClient.Planning;

public sealed record HarnessRunManifest(
    string ProtocolName,
    string ProtocolVersion,
    bool Success,
    string SessionId,
    string TaskId,
    string NextAction,
    string OutputDirectory,
    string ExecutionPlanMarkdownPath,
    string WorkerPacketMarkdownPath,
    string SessionJsonPath,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    bool UsedFallbackSearches,
    string? WorkerPacketText);

