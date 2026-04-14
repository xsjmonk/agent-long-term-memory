namespace HarnessMcp.AgentClient.Planning;

public sealed class ScopeInferenceService
{
    public PlannedChunkScopes Infer(RequirementIntent intent)
    {
        var features = intent.Feature is null
            ? Array.Empty<string>()
            : new[] { intent.Feature };

        var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in intent.CandidateLayers) if (!string.IsNullOrWhiteSpace(l)) layers.Add(l.Trim());

        var concerns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in intent.HardConstraints) if (!string.IsNullOrWhiteSpace(c)) concerns.Add(c.Trim());
        foreach (var r in intent.RiskSignals) if (!string.IsNullOrWhiteSpace(r)) concerns.Add(r.Trim());

        return new PlannedChunkScopes(
            Domain: intent.Domain,
            Module: intent.Module,
            Features: features,
            Layers: layers.ToArray(),
            Concerns: concerns.ToArray(),
            Repos: Array.Empty<string>(),
            Services: Array.Empty<string>(),
            Symbols: Array.Empty<string>());
    }
}

