using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public static class CaseShapeMatcher
{
    public static double Match(SimilarCaseShapeDto? a, SimilarCaseShapeDto? b)
    {
        if (a is null || b is null)
            return 0;

        double score = 0;
        if (string.Equals(a.TaskType, b.TaskType, StringComparison.OrdinalIgnoreCase))
            score += 0.30;
        if (string.Equals(a.FeatureShape, b.FeatureShape, StringComparison.OrdinalIgnoreCase))
            score += 0.30;
        if (a.EngineChangeAllowed == b.EngineChangeAllowed)
            score += 0.15;

        var layersA = new HashSet<string>(a.LikelyLayers, StringComparer.OrdinalIgnoreCase);
        var layersB = new HashSet<string>(b.LikelyLayers, StringComparer.OrdinalIgnoreCase);
        var layerOverlap = layersA.Count == 0 && layersB.Count == 0
            ? 0
            : (double)layersA.Intersect(layersB).Count() / Math.Max(1, Math.Max(layersA.Count, layersB.Count));
        score += 0.15 * layerOverlap;

        var risksA = new HashSet<string>(a.RiskSignals, StringComparer.OrdinalIgnoreCase);
        var risksB = new HashSet<string>(b.RiskSignals, StringComparer.OrdinalIgnoreCase);
        var riskOverlap = risksA.Count == 0 && risksB.Count == 0
            ? 0
            : (double)risksA.Intersect(risksB).Count() / Math.Max(1, Math.Max(risksA.Count, risksB.Count));
        score += 0.10 * riskOverlap;

        // Complexity is treated as exact-or-near (substring match) to protect similar-case precision.
        if (!string.IsNullOrWhiteSpace(a.Complexity) && !string.IsNullOrWhiteSpace(b.Complexity))
        {
            if (string.Equals(a.Complexity, b.Complexity, StringComparison.OrdinalIgnoreCase))
                score += 0.05;
            else if (a.Complexity.Contains(b.Complexity, StringComparison.OrdinalIgnoreCase) ||
                     b.Complexity.Contains(a.Complexity, StringComparison.OrdinalIgnoreCase))
                score += 0.03;
        }

        return Math.Clamp(score, 0, 1);
    }
}
