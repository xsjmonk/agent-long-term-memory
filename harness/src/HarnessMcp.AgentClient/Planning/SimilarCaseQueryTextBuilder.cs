using System.Text;
using System.Linq;

namespace HarnessMcp.AgentClient.Planning;

public static class SimilarCaseQueryTextBuilder
{
    public static string Build(SimilarCaseSignature signature)
    {
        // Deterministic, compact natural query: tokens joined by spaces (no JSON).
        static string NormToken(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var s = input.Trim().ToLowerInvariant();
            // Normalize separators to spaces, then to dashes for "wordy" tokens.
            s = s.Replace('_', '-').Replace('/', '-');
            // Keep common punctuation inside words; remove extra characters.
            s = new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or ' ').ToArray());
            s = s.Replace("  ", " ").Replace("  ", " ").Trim();
            return s;
        }

        var sb = new StringBuilder();

        void Add(string? token)
        {
            var t = NormToken(token);
            if (string.IsNullOrWhiteSpace(t)) return;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(t);
        }

        Add(signature.TaskType);
        Add(signature.FeatureShape);
        Add(signature.EngineChangeAllowed ? "engine-change-allowed" : "no-engine-change");

        foreach (var layer in signature.LikelyLayers.Distinct(StringComparer.OrdinalIgnoreCase).Take(6))
            Add(layer);

        foreach (var risk in signature.RiskSignals.Distinct(StringComparer.OrdinalIgnoreCase).Take(6))
            Add(NormToken(risk));

        return sb.ToString().Trim();
    }
}

