using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Planning;

public static class RequirementIntentParser
{
    private sealed record Draft(
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

    public static RequirementIntent Parse(
        string sessionId,
        string taskId,
        string rawTask,
        string modelJson)
    {
        if (!JsonHelpers.TryGetJsonObject(modelJson, out _))
            throw new InvalidOperationException("RequirementIntent model output must be a JSON object.");

        var draft = JsonHelpers.Deserialize<Draft>(modelJson);

        static IReadOnlyList<string> NonNullList(IReadOnlyList<string>? v) => v ?? Array.Empty<string>();

        return new RequirementIntent(
            SessionId: sessionId,
            TaskId: taskId,
            RawTask: rawTask,
            TaskType: draft.TaskType,
            Domain: draft.Domain,
            Module: draft.Module,
            Feature: draft.Feature,
            Goal: draft.Goal,
            RequestedOperations: NonNullList(draft.RequestedOperations),
            HardConstraints: NonNullList(draft.HardConstraints),
            SoftConstraints: NonNullList(draft.SoftConstraints),
            RiskSignals: NonNullList(draft.RiskSignals),
            CandidateLayers: NonNullList(draft.CandidateLayers),
            RetrievalFocuses: NonNullList(draft.RetrievalFocuses),
            Ambiguities: NonNullList(draft.Ambiguities),
            Complexity: draft.Complexity);
    }
}

