using System.Text.RegularExpressions;
using HarnessMcp.AgentClient.Support;
using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Planning;

public sealed class ExecutionPlanValidator
{
    public RunResult<ExecutionPlan> Validate(
        ExecutionPlan plan,
        RequirementIntent requirementIntent,
        BuildMemoryContextPackResponse contextPack)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (plan.Steps.Count == 0)
            errors.Add("Execution plan must include at least one step.");

        // Step numbering validation.
        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var expected = i + 1;
            if (plan.Steps[i].StepNumber != expected)
                errors.Add($"StepNumber invalid at index {i}: expected {expected}, got {plan.Steps[i].StepNumber}.");
        }

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var s = plan.Steps[i];
            if (s.Actions is null || s.Actions.Count == 0)
                errors.Add($"Step {s.StepNumber} must include Actions.");
            if (s.AcceptanceChecks is null || s.AcceptanceChecks.Count == 0)
                errors.Add($"Step {s.StepNumber} must include AcceptanceChecks.");

            // Forbid any worker-side memory retrieval.
            var forbiddenPhrases = new[]
            {
                "retrieve long-term memory",
                "retrieve_memory",
                "get_knowledge_item",
                "search_knowledge",
                "call mcp",
            };

            bool mentionsForbidden = forbiddenPhrases.Any(p =>
                (s.Purpose + " " + string.Join(" ", s.Inputs) + " " + string.Join(" ", s.Actions) + " " + string.Join(" ", s.AcceptanceChecks) + " " + string.Join(" ", s.Notes))
                    .IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
            if (mentionsForbidden)
                errors.Add($"Step {s.StepNumber} suggests worker-side memory retrieval.");
        }

        // Hard constraints preservation.
        var required = requirementIntent.HardConstraints.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
        var provided = plan.HardConstraints.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
        var providedSet = new HashSet<string>(provided, StringComparer.OrdinalIgnoreCase);

        foreach (var hc in required)
        {
            if (!providedSet.Contains(hc))
                errors.Add($"Plan drops required hard constraint: '{hc}'.");
        }

        // Anti-pattern preservation: if memory bundle has anti-patterns, plan must include them explicitly.
        var memoryAntiIds = contextPack.ContextPack.AntiPatterns.Select(x => x.Item.KnowledgeItemId.ToString("D")).ToArray();
        if (memoryAntiIds.Length > 0)
        {
            // We accept either explicit ids or explicit titles. Prefer ids by design.
            var antiText = string.Join(" | ", plan.AntiPatternsToAvoid);
            var anyIdPresent = memoryAntiIds.Any(id => antiText.Contains(id, StringComparison.OrdinalIgnoreCase));
            if (!anyIdPresent)
            {
                // fallback heuristic: accept if any title appears (case-insensitive)
                var memoryTitles = contextPack.ContextPack.AntiPatterns.Select(x => x.Item.Title).Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
                var anyTitlePresent = memoryTitles.Any(t => antiText.Contains(t, StringComparison.OrdinalIgnoreCase));
                if (!anyTitlePresent)
                    errors.Add("Plan omits anti-patterns while memory had anti-patterns.");
            }
        }

        if (errors.Count > 0)
            return RunResult.Failure<ExecutionPlan>(errors);

        // A few warnings for observability.
        if (plan.OpenQuestions.Count == 0)
            warnings.Add("OpenQuestions is empty.");

        return RunResult.Success(plan, warnings);
    }
}

