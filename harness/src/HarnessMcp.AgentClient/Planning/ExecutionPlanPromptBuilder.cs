using System.Text;
using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Planning;

public sealed class ExecutionPlanPromptBuilder
{
    public string BuildSystemPrompt()
    {
        return
            "You are a planning assistant. Return ONLY a single valid JSON object (no markdown).\n" +
            "The JSON must match the ExecutionPlan schema and include these top-level fields:\n" +
            "- SessionId (string)\n" +
            "- TaskId (string)\n" +
            "- Objective (string)\n" +
            "- Assumptions (array of string)\n" +
            "- HardConstraints (array of string) [must preserve all provided hard constraints verbatim]\n" +
            "- AntiPatternsToAvoid (array of string) [must include anti-patterns explicitly]\n" +
            "- Steps (array of ExecutionStep objects)\n" +
            "- ValidationChecks (array of string)\n" +
            "- Deliverables (array of string)\n" +
            "- OpenQuestions (array of string)\n\n" +
            "Each ExecutionStep object must include:\n" +
            "- StepNumber (integer)\n" +
            "- Title (string)\n" +
            "- Purpose (string)\n" +
            "- Inputs (array of string)\n" +
            "- Actions (array of string) [must be execution-ready]\n" +
            "- Outputs (array of string)\n" +
            "- AcceptanceChecks (array of string) [must be specific and testable]\n" +
            "- SupportingMemoryIds (array of string) [must reference memory item ids when applicable]\n" +
            "- Notes (array of string)\n\n" +
            "Rules:\n" +
            "1) Preserve all hard constraints exactly as provided.\n" +
            "2) Do not invent new requirements beyond the provided task/intent.\n" +
            "3) Do not ask the worker to retrieve memory again. No step may mention retrieving long-term memory.\n" +
            "4) Generate ordered, execution-ready steps.\n";
    }

    public string BuildUserPrompt(
        string rawTask,
        RequirementIntent requirementIntent,
        RetrievalChunkSet chunkSet,
        string compactMemorySummaryMarkdown)
    {
        // Keep the prompt compact and deterministic: only include chunk type ordering + ids/text, not raw MCP JSON.
        var sb = new StringBuilder();
        sb.AppendLine("RAW_TASK:");
        sb.AppendLine(rawTask);
        sb.AppendLine();

        sb.AppendLine("REQUIREMENT_INTENT_JSON:");
        sb.AppendLine(JsonHelpers.Serialize(requirementIntent));
        sb.AppendLine();

        sb.AppendLine("RETRIEVAL_CHUNKS_JSON:");
        // Only include chunk order/type/text to reduce prompt size.
        sb.AppendLine(JsonHelpers.Serialize(new
        {
            chunkSet.SessionId,
            chunkSet.TaskId,
            chunkSet.Complexity,
            chunks = chunkSet.Chunks.Select(c => new { c.ChunkId, c.ChunkType, c.Text, scopes = new { c.Scopes.Domain, c.Scopes.Module, c.Scopes.Layers } })
        }));
        sb.AppendLine();

        sb.AppendLine("PLANNING_MEMORY_SUMMARY_MARKDOWN:");
        sb.AppendLine(compactMemorySummaryMarkdown);
        sb.AppendLine();

        return sb.ToString();
    }
}

