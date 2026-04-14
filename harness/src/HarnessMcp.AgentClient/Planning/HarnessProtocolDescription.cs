namespace HarnessMcp.AgentClient.Planning;

public sealed record HarnessProtocolDescription(
    string ProtocolName,
    string ProtocolVersion,
    string PrimaryCommand,
    IReadOnlyList<ProtocolArgument> RequiredArguments,
    IReadOnlyList<ProtocolArgument> OptionalArguments,
    IReadOnlyList<string> OutputArtifacts,
    IReadOnlyList<string> StdoutFields,
    IReadOnlyList<string> RequiredAgentBehavior,
    IReadOnlyList<string> ForbiddenAgentBehavior,
    string SuccessExitMeaning,
    string FailureExitMeaning,
    string NextActionOnSuccess);

public sealed record ProtocolArgument(
    string Name,
    bool Required,
    string Description);

