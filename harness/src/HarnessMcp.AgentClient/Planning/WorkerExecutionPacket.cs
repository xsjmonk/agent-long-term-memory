namespace HarnessMcp.AgentClient.Planning;

public sealed record WorkerExecutionPacket(
    string SessionId,
    string TaskId,
    string Objective,
    IReadOnlyList<string> AllowedScope,
    IReadOnlyList<string> ForbiddenActions,
    IReadOnlyList<string> HardConstraints,
    IReadOnlyList<string> KeyMemory,
    IReadOnlyList<ExecutionStep> Steps,
    IReadOnlyList<string> RequiredOutputSections);

