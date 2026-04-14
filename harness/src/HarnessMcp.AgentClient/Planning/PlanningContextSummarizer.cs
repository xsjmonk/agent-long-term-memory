using System.Text;
using HarnessMcp.AgentClient.Artifacts;
using HarnessMcp.AgentClient.Support;
using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Planning;

public sealed class PlanningContextSummarizer
{
    public string Summarize(PlanningMemoryBundle bundle)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Planning Memory Summary");
        sb.AppendLine();
        sb.AppendLine(bundle.UsedFallbackSearches
            ? "- Fallback searches were used to fill retrieval gaps."
            : "- No fallback searches were used.");
        sb.AppendLine();

        void Section(string title, IEnumerable<MergedKnowledgeItemDto> items, Func<SearchKnowledgeResponse, IEnumerable<KnowledgeCandidateDto>>? fallbackCandidatesSelector, RetrievalClass bucketClass)
        {
            sb.AppendLine($"## {title}");
            var list = items.ToList();

            if (list.Count == 0 && bundle.UsedFallbackSearches)
            {
                var fallback = bundle.FallbackSearches.SelectMany(fallbackCandidatesSelector ?? (f => f.Candidates))
                    .Where(c => c.RetrievalClass == bucketClass)
                    .OrderByDescending(c => c.FinalScore)
                    .ThenBy(c => c.KnowledgeItemId)
                    .Take(3)
                    .ToList();

                if (fallback.Count == 0)
                {
                    sb.AppendLine("- (empty)");
                    sb.AppendLine();
                    return;
                }

                foreach (var c in fallback)
                {
                    sb.AppendLine($"- {c.KnowledgeItemId}: {c.Title}");
                }
                sb.AppendLine();
                return;
            }

            if (list.Count == 0)
            {
                sb.AppendLine("- (empty)");
                sb.AppendLine();
                return;
            }

            foreach (var i in list.OrderBy(x => x.Item.KnowledgeItemId).Take(3))
                sb.AppendLine($"- {i.Item.KnowledgeItemId}: {i.Item.Title}");

            sb.AppendLine();
        }

        Section(
            "Decisions",
            bundle.ContextPack.ContextPack.Decisions,
            f => f.Candidates,
            RetrievalClass.Decision);

        Section(
            "Constraints",
            bundle.ContextPack.ContextPack.Constraints,
            f => f.Candidates,
            RetrievalClass.Constraint);

        Section(
            "Best Practices",
            bundle.ContextPack.ContextPack.BestPractices,
            f => f.Candidates,
            RetrievalClass.BestPractice);

        Section(
            "Anti-Patterns",
            bundle.ContextPack.ContextPack.AntiPatterns,
            f => f.Candidates,
            RetrievalClass.Antipattern);

        Section(
            "Similar Cases",
            bundle.ContextPack.ContextPack.SimilarCases,
            f => f.Candidates,
            RetrievalClass.SimilarCase);

        sb.AppendLine("## Warnings");
        var warnings = bundle.ContextPack.Diagnostics.Warnings
            .Concat(bundle.Diagnostics)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToList();

        if (warnings.Count == 0)
            sb.AppendLine("- (none)");
        else
        {
            foreach (var w in warnings.OrderBy(w => w))
                sb.AppendLine($"- {w}");
        }

        return sb.ToString();
    }
}

