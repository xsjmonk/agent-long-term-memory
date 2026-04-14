using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Config;

public sealed record AgentClientOptions(
    string? TaskFile,
    string? TaskText,
    string OutputDir,
    string McpBaseUrl,
    string ModelBaseUrl,
    string ModelName,
    string ApiKeyEnv,
    string? SessionId,
    string? Project,
    string? Domain,
    int MaxItemsPerChunk,
    AuthorityLevel MinimumAuthority,
    bool EmitIntermediates,
    bool StdoutJson,
    bool PrintWorkerPacket);

