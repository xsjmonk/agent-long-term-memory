using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Planning;

public sealed class RequirementIntentValidator
{
    private static readonly HashSet<string> AllowedComplexities = new(StringComparer.OrdinalIgnoreCase)
    {
        "low", "medium", "high"
    };

    public RunResult<RequirementIntent> Validate(RequirementIntent intent)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(intent.TaskType))
            errors.Add("TaskType is required.");

        if (string.IsNullOrWhiteSpace(intent.Goal))
            errors.Add("Goal is required.");

        if (string.IsNullOrWhiteSpace(intent.Complexity))
            errors.Add("Complexity is required.");
        else if (!AllowedComplexities.Contains(intent.Complexity.Trim()))
            errors.Add($"Complexity must be one of: low, medium, high. Got '{intent.Complexity}'.");

        if (intent.RequestedOperations is null) errors.Add("RequestedOperations must never be null.");
        if (intent.HardConstraints is null) errors.Add("HardConstraints must never be null.");
        if (intent.SoftConstraints is null) errors.Add("SoftConstraints must never be null.");
        if (intent.RiskSignals is null) errors.Add("RiskSignals must never be null.");
        if (intent.CandidateLayers is null) errors.Add("CandidateLayers must never be null.");
        if (intent.RetrievalFocuses is null) errors.Add("RetrievalFocuses must never be null.");
        if (intent.Ambiguities is null) errors.Add("Ambiguities must never be null.");

        if (errors.Count > 0)
            return RunResult.Failure<RequirementIntent>(errors, warnings);

        return RunResult.Success(intent, warnings);
    }
}

