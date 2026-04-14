using System.Text;
using HarnessMcp.AgentClient.Support;
using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Planning;

public sealed class WorkerPacketBuilder
{
    public WorkerExecutionPacket Build(
        RequirementIntent requirementIntent,
        ExecutionPlan executionPlan,
        IReadOnlyList<GetKnowledgeItemResponse> hydratedItems)
    {
        var allowedScope = new List<string>();
        if (!string.IsNullOrWhiteSpace(requirementIntent.Domain)) allowedScope.Add("domain:" + requirementIntent.Domain);
        if (!string.IsNullOrWhiteSpace(requirementIntent.Module)) allowedScope.Add("module:" + requirementIntent.Module);
        if (!string.IsNullOrWhiteSpace(requirementIntent.Feature)) allowedScope.Add("feature:" + requirementIntent.Feature);

        // Allowed scope layers come only from CandidateLayers (not RetrievalFocuses).
        foreach (var l in requirementIntent.CandidateLayers.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.OrdinalIgnoreCase))
            allowedScope.Add("layer:" + l.Trim());

        var forbiddenActions = new List<string>
        {
            "do not retrieve long-term memory independently",
            "do not reinterpret the task at architecture level",
            "do not expand scope beyond the listed steps",
            "do not change forbidden layers/components",
            "if blocked by ambiguity, report the block instead of inventing behavior"
        };

        var keyMemory = DistillKeyMemoryBullets(hydratedItems);
        var requiredOutputSections = new List<string>
        {
            "Per-step execution results (one section per step)",
            "Final deliverables (from Deliverables)",
            "Validation summary (pass/fail against AcceptanceChecks)"
        };

        return new WorkerExecutionPacket(
            SessionId: executionPlan.SessionId,
            TaskId: executionPlan.TaskId,
            Objective: executionPlan.Objective,
            AllowedScope: allowedScope,
            ForbiddenActions: forbiddenActions,
            HardConstraints: executionPlan.HardConstraints,
            KeyMemory: keyMemory,
            Steps: executionPlan.Steps,
            RequiredOutputSections: requiredOutputSections);
    }

    private static IReadOnlyList<string> DistillKeyMemoryBullets(IReadOnlyList<GetKnowledgeItemResponse> hydratedItems)
    {
        var ordered = hydratedItems
            .OrderByDescending(h => h.Item.FinalScore)
            .ThenBy(h => h.Item.KnowledgeItemId)
            .ToList();

        IEnumerable<GetKnowledgeItemResponse> TakeTop(RetrievalClass cls) =>
            ordered.Where(h => h.Item.RetrievalClass == cls).Take(3);

        var bullets = new List<string>();
        foreach (var dec in TakeTop(RetrievalClass.Decision))
            bullets.Add($"{dec.Item.KnowledgeItemId:D}: decision - {dec.Item.Title}");
        foreach (var con in TakeTop(RetrievalClass.Constraint))
            bullets.Add($"{con.Item.KnowledgeItemId:D}: constraint - {con.Item.Title}");
        foreach (var best in TakeTop(RetrievalClass.BestPractice))
            bullets.Add($"{best.Item.KnowledgeItemId:D}: best practice - {best.Item.Title}");
        foreach (var anti in TakeTop(RetrievalClass.Antipattern))
            bullets.Add($"{anti.Item.KnowledgeItemId:D}: anti-pattern - {anti.Item.Title}");
        foreach (var sim in TakeTop(RetrievalClass.SimilarCase))
            bullets.Add($"{sim.Item.KnowledgeItemId:D}: similar case - {sim.Item.Title}");

        return bullets;
    }
}

