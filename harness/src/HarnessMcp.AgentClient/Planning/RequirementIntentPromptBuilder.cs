namespace HarnessMcp.AgentClient.Planning;

public static class RequirementIntentPromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return
            "You are a planning assistant. Return ONLY a single valid JSON object. No markdown, no commentary.\n" +
            "The JSON must contain these top-level fields:\n" +
            "- TaskType (string, required)\n" +
            "- Domain (string or null)\n" +
            "- Module (string or null)\n" +
            "- Feature (string or null)\n" +
            "- Goal (string, required)\n" +
            "- RequestedOperations (array of string, never null)\n" +
            "- HardConstraints (array of string, never null)\n" +
            "- SoftConstraints (array of string, never null)\n" +
            "- RiskSignals (array of string, never null)\n" +
            "- CandidateLayers (array of string, never null)\n" +
            "- RetrievalFocuses (array of string, never null)\n" +
            "- Ambiguities (array of string, never null)\n" +
            "- Complexity (string, required; must be one of: low | medium | high)\n" +
            "\nValidation rules you must follow:\n" +
            "1) TaskType, Goal, Complexity are required and non-empty.\n" +
            "2) Complexity must be exactly one of: low, medium, high.\n" +
            "3) Every list field must be an array ([]) when empty; never null.\n" +
            "4) Preserve ambiguities explicitly by putting uncertain or missing details into Ambiguities (do not drop them).\n";
    }

    public static string BuildUserPrompt(string rawTask, string? project, string? domain)
    {
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(project)) meta.Add($"project={project}");
        if (!string.IsNullOrWhiteSpace(domain)) meta.Add($"domain={domain}");

        var metaLine = meta.Count == 0 ? "(none)" : string.Join(", ", meta);
        return
            "RAW_TASK:\n" +
            rawTask + "\n\n" +
            "CLI_METADATA:\n" +
            metaLine + "\n\n" +
            "Return the JSON object as specified.";
    }
}

