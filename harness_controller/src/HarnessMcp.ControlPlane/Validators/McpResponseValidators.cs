using System.Text.Json;

namespace HarnessMcp.ControlPlane.Validators;

// Validates retrieve_memory_by_chunks MCP response.
// Accepts camelCase (MCP native: taskId, chunkResults, chunkId, chunkType)
// OR snake_case (manually crafted: task_id, chunk_results, chunk_id, chunk_type).
public class RetrieveMemoryByChunksResponseValidator
{
    public ValidationResult Validate(object? value)
    {
        var errors = new List<string>();

        if (value == null)
            return new ValidationResult { IsValid = false, Errors = new() { "response is required" } };

        if (value is not JsonElement element)
            return new ValidationResult { IsValid = false, Errors = new() { "RetrieveMemoryByChunksResponse must be a JSON object" } };

        // Accept taskId (MCP camelCase) or task_id (snake_case)
        var hasTaskId = element.TryGetProperty("taskId", out var taskId) || element.TryGetProperty("task_id", out taskId);
        if (!hasTaskId || string.IsNullOrEmpty(taskId.GetString()))
            errors.Add("taskId (or task_id) is required");

        // Accept chunkResults (MCP camelCase) or chunk_results (snake_case)
        var hasChunks = element.TryGetProperty("chunkResults", out var cr) || element.TryGetProperty("chunk_results", out cr);
        if (!hasChunks || cr.ValueKind != JsonValueKind.Array || cr.GetArrayLength() == 0)
        {
            errors.Add("chunkResults (or chunk_results) array is required and must not be empty");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        foreach (var item in cr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add("each chunk result must be an object");
                continue;
            }
            // Accept chunkId or chunk_id
            var hasChunkId = item.TryGetProperty("chunkId", out var chunkId) || item.TryGetProperty("chunk_id", out chunkId);
            if (!hasChunkId || string.IsNullOrEmpty(chunkId.GetString()))
                errors.Add("each chunk result must have chunkId (or chunk_id)");

            // results object is required (same name in both formats)
            if (!item.TryGetProperty("results", out var resultsObj) || resultsObj.ValueKind != JsonValueKind.Object)
                errors.Add($"chunk result '{(hasChunkId ? chunkId.GetString() : "?")}' must have results object");
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors, Warnings = new() };
    }
}

// Validates merge_retrieval_results MCP response.
// MCP returns buckets flat at top level (taskId, decisions[], constraints[], bestPractices[], ...).
// Also accepts the old wrapped format: task_id + merged { decisions[], ... }.
public class MergeRetrievalResultsResponseValidator
{
    private static readonly string[] McpBuckets = { "decisions", "constraints", "bestPractices", "antiPatterns", "similarCases", "references", "structures" };
    private static readonly string[] SnakeBuckets = { "decisions", "constraints", "best_practices", "anti_patterns", "similar_cases", "references", "structures" };

    public ValidationResult Validate(object? value)
    {
        var errors = new List<string>();

        if (value == null)
            return new ValidationResult { IsValid = false, Errors = new() { "response is required" } };

        if (value is not JsonElement element)
            return new ValidationResult { IsValid = false, Errors = new() { "MergeRetrievalResultsResponse must be a JSON object" } };

        // Accept taskId (MCP) or task_id (snake_case)
        var hasTaskId = element.TryGetProperty("taskId", out var taskId) || element.TryGetProperty("task_id", out taskId);
        if (!hasTaskId || string.IsNullOrEmpty(taskId.GetString()))
            errors.Add("taskId (or task_id) is required");

        // MCP format: buckets at top level
        var hasMcpBuckets = McpBuckets.All(b => element.TryGetProperty(b, out var v) && v.ValueKind == JsonValueKind.Array);
        if (hasMcpBuckets)
            return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };

        // Wrapped format: merged { decisions[], ... } with snake_case
        if (element.TryGetProperty("merged", out var merged) && merged.ValueKind == JsonValueKind.Object)
        {
            foreach (var bucket in SnakeBuckets)
            {
                if (!merged.TryGetProperty(bucket, out var arr) || arr.ValueKind != JsonValueKind.Array)
                    errors.Add($"merged must contain {bucket} bucket");
            }
            return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
        }

        errors.Add("expected either MCP flat format (taskId + top-level bucket arrays) or wrapped format (task_id + merged object)");
        return new ValidationResult { IsValid = false, Errors = errors };
    }
}

// Validates build_memory_context_pack MCP response.
// MCP returns: taskId + contextPack { decisions[], bestPractices[], ... }.
// Also accepts: task_id + memory_context_pack { must_follow[], best_practices[], avoid[], similar_case_guidance[], retrieval_support{} }.
public class BuildMemoryContextPackResponseValidator
{
    public ValidationResult Validate(object? value)
    {
        var errors = new List<string>();

        if (value == null)
            return new ValidationResult { IsValid = false, Errors = new() { "response is required" } };

        if (value is not JsonElement element)
            return new ValidationResult { IsValid = false, Errors = new() { "BuildMemoryContextPackResponse must be a JSON object" } };

        // Accept taskId (MCP) or task_id (snake_case)
        var hasTaskId = element.TryGetProperty("taskId", out var taskId) || element.TryGetProperty("task_id", out taskId);
        if (!hasTaskId || string.IsNullOrEmpty(taskId.GetString()))
            errors.Add("taskId (or task_id) is required");

        // MCP format: contextPack object
        if (element.TryGetProperty("contextPack", out var cp) && cp.ValueKind == JsonValueKind.Object)
            return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };

        // Snake_case format: memory_context_pack object
        if (element.TryGetProperty("memory_context_pack", out var mcp) && mcp.ValueKind == JsonValueKind.Object)
            return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };

        errors.Add("expected contextPack (MCP format) or memory_context_pack (snake_case format) object");
        return new ValidationResult { IsValid = false, Errors = errors };
    }
}
