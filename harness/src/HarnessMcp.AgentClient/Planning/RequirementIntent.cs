namespace HarnessMcp.AgentClient.Planning;

public sealed record RequirementIntent(
    string SessionId,
    string TaskId,
    string RawTask,
    string TaskType,
    string? Domain,
    string? Module,
    string? Feature,
    string Goal,
    IReadOnlyList<string> RequestedOperations,
    IReadOnlyList<string> HardConstraints,
    IReadOnlyList<string> SoftConstraints,
    IReadOnlyList<string> RiskSignals,
    IReadOnlyList<string> CandidateLayers,
    IReadOnlyList<string> RetrievalFocuses,
    IReadOnlyList<string> Ambiguities,
    string Complexity);

