namespace HarnessMcp.AgentClient.Planning;

public sealed record ExecutionPlan(
    string SessionId,
    string TaskId,
    string Objective,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> HardConstraints,
    IReadOnlyList<string> AntiPatternsToAvoid,
    IReadOnlyList<ExecutionStep> Steps,
    IReadOnlyList<string> ValidationChecks,
    IReadOnlyList<string> Deliverables,
    IReadOnlyList<string> OpenQuestions);

public sealed record ExecutionStep(
    int StepNumber,
    string Title,
    string Purpose,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<string> AcceptanceChecks,
    IReadOnlyList<string> SupportingMemoryIds,
    IReadOnlyList<string> Notes);

