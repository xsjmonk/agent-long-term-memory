using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarnessMcp.ControlPlane.Validators;

public class RetrieveMemoryByChunksResponseValidator
{
    private static readonly string[] RequiredBuckets = { "decisions", "best_practices", "anti_patterns", "similar_cases", "constraints", "references", "structures" };

    public ValidationResult Validate(object? value)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (value == null)
            return new ValidationResult { IsValid = false, Errors = new() { "response is required" } };

        if (value is not JsonElement element)
        {
            return new ValidationResult { IsValid = false, Errors = new() { "RetrieveMemoryByChunksResponse must be a JSON object" } };
        }

        if (!element.TryGetProperty("task_id", out var taskId) || string.IsNullOrEmpty(taskId.GetString()))
            errors.Add("task_id is required");

        if (element.TryGetProperty("results", out _))
            errors.Add("use 'chunk_results' field name, not 'results'");

        if (element.TryGetProperty("retrieved", out _))
            errors.Add("use 'chunk_results' field name, not 'retrieved'");

        if (!element.TryGetProperty("chunk_results", out var cr) || cr.ValueKind != JsonValueKind.Array || cr.GetArrayLength() == 0)
        {
            errors.Add("chunk_results array is required and must not be empty");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        foreach (var item in cr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add("each chunk result must be an object");
                continue;
            }
            if (!item.TryGetProperty("chunk_id", out var chunkId) || string.IsNullOrEmpty(chunkId.GetString()))
                errors.Add("each chunk result must have chunk_id");

            if (!item.TryGetProperty("chunk_type", out var chunkType) || string.IsNullOrEmpty(chunkType.GetString()))
                errors.Add("each chunk result must have chunk_type");

            if (!item.TryGetProperty("results", out var resultsObj) || resultsObj.ValueKind != JsonValueKind.Object)
            {
                errors.Add("each chunk result must have results object");
                continue;
            }

            foreach (var requiredBucket in RequiredBuckets)
            {
                if (!resultsObj.TryGetProperty(requiredBucket, out var bucket) || bucket.ValueKind != JsonValueKind.Array)
                    errors.Add($"results must contain {requiredBucket} bucket");
            }

            foreach (var resultProp in resultsObj.EnumerateObject())
            {
                if (!RequiredBuckets.Contains(resultProp.Name))
                    errors.Add($"unknown bucket '{resultProp.Name}' in results");
                else if (resultProp.Value.ValueKind != JsonValueKind.Array)
                    errors.Add($"bucket '{resultProp.Name}' must be an array");
                else
                {
                    foreach (var candidate in resultProp.Value.EnumerateArray())
                    {
                        if (candidate.ValueKind != JsonValueKind.Object)
                        {
                            errors.Add($"each candidate in {resultProp.Name} must be an object");
                            continue;
                        }
                        if (!candidate.TryGetProperty("knowledge_item_id", out var kid) || string.IsNullOrEmpty(kid.GetString()))
                        {
                            if (candidate.TryGetProperty("memory_id", out _))
                                errors.Add($"use 'knowledge_item_id' not 'memory_id' in candidate");
                            else
                                errors.Add($"candidate must have knowledge_item_id");
                        }
                        if (!candidate.TryGetProperty("title", out var title) || string.IsNullOrEmpty(title.GetString()))
                            errors.Add($"candidate must have title");
                        if (!candidate.TryGetProperty("summary", out var summary) || string.IsNullOrEmpty(summary.GetString()))
                            errors.Add($"candidate must have summary");
                    }
                }
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors, Warnings = warnings };
    }
}

public class MergeRetrievalResultsResponseValidator
{
    private static readonly string[] CanonicalBuckets = new[]
    {
        "decisions", "constraints", "best_practices",
        "anti_patterns", "similar_cases", "references", "structures"
    };

    public ValidationResult Validate(object? value)
    {
        var errors = new List<string>();

        if (value == null)
            return new ValidationResult { IsValid = false, Errors = new() { "response is required" } };

        if (value is not JsonElement element)
        {
            return new ValidationResult { IsValid = false, Errors = new() { "MergeRetrievalResultsResponse must be a JSON object" } };
        }

        if (!element.TryGetProperty("task_id", out var taskId) || string.IsNullOrEmpty(taskId.GetString()))
            errors.Add("task_id is required");

        if (element.TryGetProperty("results", out _))
            errors.Add("use 'merged' field name, not 'results'");

        if (element.TryGetProperty("merged_results", out _))
            errors.Add("use 'merged' field name, not 'merged_results'");

        if (element.TryGetProperty("memory_pack", out _))
            errors.Add("use 'merged' field name, not 'memory_pack'");

        if (element.TryGetProperty("context_pack", out _))
            errors.Add("use 'merged' field name, not 'context_pack'");

        if (!element.TryGetProperty("merged", out var merged) || merged.ValueKind != JsonValueKind.Object)
        {
            errors.Add("merged object is required");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        foreach (var bucket in CanonicalBuckets)
        {
            if (!merged.TryGetProperty(bucket, out var arr) || arr.ValueKind != JsonValueKind.Array)
                errors.Add($"merged must contain {bucket} bucket");
        }

        foreach (var bucketProp in merged.EnumerateObject())
        {
            if (!CanonicalBuckets.Contains(bucketProp.Name))
                errors.Add($"unknown bucket '{bucketProp.Name}' in merged");
            else if (bucketProp.Value.ValueKind != JsonValueKind.Array)
                errors.Add($"bucket '{bucketProp.Name}' must be an array");
            else
            {
                foreach (var item in bucketProp.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add($"each merged item must be an object");
                        continue;
                    }
                    if (!item.TryGetProperty("item", out var it) || it.ValueKind != JsonValueKind.Object)
                        errors.Add("merged item must have 'item' object");
                    else
                    {
                        if (!it.TryGetProperty("knowledge_item_id", out var kid) || string.IsNullOrEmpty(kid.GetString()))
                            errors.Add("item must have knowledge_item_id");
                        if (!it.TryGetProperty("title", out var title) || string.IsNullOrEmpty(title.GetString()))
                            errors.Add("item must have title");
                        if (!it.TryGetProperty("summary", out var summary) || string.IsNullOrEmpty(summary.GetString()))
                            errors.Add("item must have summary");
                    }
                    if (!item.TryGetProperty("supported_by_chunk_ids", out var scids) || scids.ValueKind != JsonValueKind.Array)
                        errors.Add("merged item must have supported_by_chunk_ids");
                    if (!item.TryGetProperty("supported_by_chunk_types", out var scts) || scts.ValueKind != JsonValueKind.Array)
                        errors.Add("merged item must have supported_by_chunk_types");
                    if (!item.TryGetProperty("merge_rationales", out var mr) || mr.ValueKind != JsonValueKind.Array)
                        errors.Add("merged item must have merge_rationales");
                }
            }
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }
}

public class BuildMemoryContextPackResponseValidator
{
    private static readonly string[] RequiredPackSections = { "must_follow", "best_practices", "avoid", "similar_case_guidance", "retrieval_support" };
    private static readonly string[] RequiredRetrievalSupport = { "multi_supported_items", "single_route_important_items" };

    public ValidationResult Validate(object? value)
    {
        var errors = new List<string>();

        if (value == null)
            return new ValidationResult { IsValid = false, Errors = new() { "response is required" } };

        if (value is not JsonElement element)
        {
            return new ValidationResult { IsValid = false, Errors = new() { "BuildMemoryContextPackResponse must be a JSON object" } };
        }

        if (!element.TryGetProperty("task_id", out var taskId) || string.IsNullOrEmpty(taskId.GetString()))
            errors.Add("task_id is required");

        if (element.TryGetProperty("context_pack", out _))
        {
            errors.Add("use 'memory_context_pack' field name, not 'context_pack'");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        if (!element.TryGetProperty("memory_context_pack", out var mcp) || mcp.ValueKind != JsonValueKind.Object)
        {
            errors.Add("memory_context_pack object is required");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        foreach (var section in RequiredPackSections)
        {
            if (!mcp.TryGetProperty(section, out var field) || field.ValueKind != JsonValueKind.Array)
                errors.Add($"memory_context_pack.{section} array is required");
        }

        if (mcp.TryGetProperty("retrieval_support", out var rs) && rs.ValueKind == JsonValueKind.Object)
        {
            foreach (var subfield in RequiredRetrievalSupport)
            {
                if (!rs.TryGetProperty(subfield, out var sf) || sf.ValueKind != JsonValueKind.Array)
                    errors.Add($"memory_context_pack.retrieval_support.{subfield} array is required");
            }
        }
        else
        {
            errors.Add("memory_context_pack.retrieval_support object is required");
        }

        return new ValidationResult { IsValid = errors.Count == 0, Errors = errors };
    }
}