using System.Text;
using HarnessMcp.AgentClient.Planning;

namespace HarnessMcp.AgentClient.Artifacts;

public static class MarkdownRenderers
{
    public static string RenderExecutionPlanMarkdown(ExecutionPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Execution Plan");
        sb.AppendLine();
        sb.AppendLine($"**Objective:** {plan.Objective}");
        sb.AppendLine();

        if (plan.HardConstraints.Count > 0)
        {
            sb.AppendLine("## Hard Constraints");
            foreach (var c in plan.HardConstraints)
                sb.AppendLine($"- {c}");
            sb.AppendLine();
        }

        if (plan.AntiPatternsToAvoid.Count > 0)
        {
            sb.AppendLine("## Anti-Patterns To Avoid");
            foreach (var a in plan.AntiPatternsToAvoid)
                sb.AppendLine($"- {a}");
            sb.AppendLine();
        }

        sb.AppendLine("## Steps");
        foreach (var step in plan.Steps)
        {
            sb.AppendLine();
            sb.AppendLine($"### Step {step.StepNumber}: {step.Title}");
            sb.AppendLine();
            sb.AppendLine($"**Purpose:** {step.Purpose}");
            if (step.Inputs.Count > 0)
            {
                sb.AppendLine("**Inputs:**");
                foreach (var i in step.Inputs) sb.AppendLine($"- {i}");
            }
            if (step.Actions.Count > 0)
            {
                sb.AppendLine("**Actions:**");
                foreach (var a in step.Actions) sb.AppendLine($"- {a}");
            }
            if (step.Outputs.Count > 0)
            {
                sb.AppendLine("**Outputs:**");
                foreach (var o in step.Outputs) sb.AppendLine($"- {o}");
            }
            if (step.AcceptanceChecks.Count > 0)
            {
                sb.AppendLine("**Acceptance Checks:**");
                foreach (var ac in step.AcceptanceChecks) sb.AppendLine($"- {ac}");
            }
            if (step.SupportingMemoryIds.Count > 0)
            {
                sb.AppendLine("**Supporting Memory Ids:**");
                foreach (var mid in step.SupportingMemoryIds) sb.AppendLine($"- {mid}");
            }
            if (step.Notes.Count > 0)
            {
                sb.AppendLine("**Notes:**");
                foreach (var n in step.Notes) sb.AppendLine($"- {n}");
            }
        }

        if (plan.ValidationChecks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Validation Checks");
            foreach (var v in plan.ValidationChecks) sb.AppendLine($"- {v}");
        }

        if (plan.Deliverables.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Deliverables");
            foreach (var d in plan.Deliverables) sb.AppendLine($"- {d}");
        }

        if (plan.OpenQuestions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Open Questions");
            foreach (var q in plan.OpenQuestions) sb.AppendLine($"- {q}");
        }

        return sb.ToString();
    }

    public static string RenderWorkerPacketMarkdown(WorkerExecutionPacket packet)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Worker Execution Packet");
        sb.AppendLine();
        sb.AppendLine($"**Objective:** {packet.Objective}");
        sb.AppendLine();

        sb.AppendLine("## Allowed Scope");
        if (packet.AllowedScope.Count == 0) sb.AppendLine("- (none)");
        foreach (var s in packet.AllowedScope) sb.AppendLine($"- {s}");
        sb.AppendLine();

        sb.AppendLine("## Forbidden Actions");
        foreach (var f in packet.ForbiddenActions) sb.AppendLine($"- {f}");
        sb.AppendLine();

        sb.AppendLine("## Hard Constraints");
        if (packet.HardConstraints.Count == 0) sb.AppendLine("- (none)");
        foreach (var c in packet.HardConstraints) sb.AppendLine($"- {c}");
        sb.AppendLine();

        sb.AppendLine("## Key Memory");
        if (packet.KeyMemory.Count == 0) sb.AppendLine("- (none)");
        foreach (var m in packet.KeyMemory) sb.AppendLine(m.StartsWith("-") ? m : $"- {m}");
        sb.AppendLine();

        sb.AppendLine("## Ordered Steps");
        foreach (var step in packet.Steps)
        {
            sb.AppendLine();
            sb.AppendLine($"### Step {step.StepNumber}: {step.Title}");
            sb.AppendLine();
            sb.AppendLine($"**Purpose:** {step.Purpose}");
            if (step.Inputs.Count > 0)
            {
                sb.AppendLine("**Inputs:**");
                foreach (var i in step.Inputs) sb.AppendLine($"- {i}");
            }
            sb.AppendLine("**Actions:**");
            foreach (var a in step.Actions) sb.AppendLine($"- {a}");
            if (step.Outputs.Count > 0)
            {
                sb.AppendLine("**Outputs:**");
                foreach (var o in step.Outputs) sb.AppendLine($"- {o}");
            }
            sb.AppendLine("**Acceptance Checks:**");
            foreach (var ac in step.AcceptanceChecks) sb.AppendLine($"- {ac}");

            if (step.SupportingMemoryIds.Count > 0)
            {
                sb.AppendLine("**Supporting Memory Ids:**");
                foreach (var mid in step.SupportingMemoryIds) sb.AppendLine($"- {mid}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Required Output Sections");
        foreach (var sec in packet.RequiredOutputSections)
            sb.AppendLine($"- {sec}");

        return sb.ToString();
    }
}

