using System.Text.Json;

namespace HarnessMcp.ControlPlane.Validators;

public class RetrievalChunkSetValidator
{
    private static readonly string[] ValidChunkTypes = { "core_task", "constraint", "risk", "pattern", "similar_case" };
    private readonly ValidationOptions _options;

    public RetrievalChunkSetValidator(ValidationOptions options)
    {
        _options = options;
    }

    public ValidationResult Validate(object? value, object? requirementIntent)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (value is not JsonElement element)
        {
            return new ValidationResult { IsValid = false, Errors = new() { "RetrievalChunkSet must be a JSON object" } };
        }

        if (!element.TryGetProperty("task_id", out var taskId) || string.IsNullOrEmpty(taskId.GetString()))
            errors.Add("task_id is required and must be a non-empty string");

        if (!element.TryGetProperty("complexity", out var complexity) || string.IsNullOrEmpty(complexity.GetString()))
            errors.Add("complexity is required (low|medium|high)");
        else
        {
            var complexityStr = complexity.GetString();
            if (complexityStr != "low" && complexityStr != "medium" && complexityStr != "high")
                errors.Add("complexity must be one of: low, medium, high");
        }

        if (!element.TryGetProperty("chunks", out var chunksElement) || chunksElement.ValueKind != JsonValueKind.Array)
        {
            return new ValidationResult { IsValid = false, Errors = new() { "chunks array is required" } };
        }

        var chunks = chunksElement.EnumerateArray().ToList();
        if (chunks.Count == 0)
            return new ValidationResult { IsValid = false, Errors = new() { "at least one chunk is required" } };

        if (chunks.Count > _options.MaxChunks)
            warnings.Add($"chunk count {chunks.Count} exceeds recommended max {_options.MaxChunks}");

        var chunkIds = new HashSet<string>();
        var hasCoreTaskChunk = false;
        var hasConstraintChunk = false;
        var hasRiskChunk = false;
        var hasSimilarCaseChunk = false;
        var hasHardConstraints = false;
        var hasRiskSignals = false;
        var complexityLevel = "";

        if (requirementIntent is JsonElement riEl)
        {
            if (riEl.TryGetProperty("complexity", out var c) && c.ValueKind == JsonValueKind.String)
                complexityLevel = c.GetString() ?? "";
            hasHardConstraints = riEl.TryGetProperty("hard_constraints", out var hc) && hc.ValueKind == JsonValueKind.Array && hc.GetArrayLength() > 0;
            hasRiskSignals = riEl.TryGetProperty("risk_signals", out var rs) && rs.ValueKind == JsonValueKind.Array && rs.GetArrayLength() > 0;
        }

        foreach (var chunk in chunks)
        {
            if (chunk.ValueKind != JsonValueKind.Object)
            {
                errors.Add("each chunk must be an object");
                continue;
            }

            if (!chunk.TryGetProperty("chunk_id", out var chunkIdEl))
            {
                errors.Add("each chunk must have chunk_id");
                continue;
            }
            var chunkId = chunkIdEl.GetString();
            if (string.IsNullOrEmpty(chunkId))
            {
                errors.Add("chunk_id must be non-empty");
                continue;
            }
            if (!chunkIds.Add(chunkId))
                errors.Add($"duplicate chunk_id: {chunkId}");

            if (!chunk.TryGetProperty("chunk_type", out var chunkTypeEl) || string.IsNullOrEmpty(chunkTypeEl.GetString()))
            {
                errors.Add($"chunk {chunkId} must have chunk_type");
                continue;
            }
            var chunkType = chunkTypeEl.GetString();
            if (!ValidChunkTypes.Contains(chunkType!))
            {
                errors.Add($"chunk {chunkId} chunk_type must be one of: {string.Join(", ", ValidChunkTypes)}");
            }

            switch (chunkType)
            {
                case "core_task":
                    hasCoreTaskChunk = true;
                    if (!chunk.TryGetProperty("text", out var text) || string.IsNullOrEmpty(text.GetString()))
                        errors.Add($"chunk {chunkId} of type core_task must have non-empty text");
                    else if (text.GetString()!.Length > _options.ChunkTextMaxLength)
                        errors.Add($"chunk {chunkId} text exceeds max length of {_options.ChunkTextMaxLength}");
                    break;
                case "constraint":
                    hasConstraintChunk = true;
                    if (!chunk.TryGetProperty("text", out var ctext) || string.IsNullOrEmpty(ctext.GetString()))
                        errors.Add($"chunk {chunkId} of type constraint must have non-empty text");
                    else if (ctext.GetString()!.Length > _options.ChunkTextMaxLength)
                        errors.Add($"chunk {chunkId} text exceeds max length of {_options.ChunkTextMaxLength}");
                    else if (ContainsPurityViolation(ctext.GetString()!, "constraint"))
                        errors.Add($"chunk {chunkId} constraint text contains pattern/feature language");
                    break;
                case "risk":
                    hasRiskChunk = true;
                    if (!chunk.TryGetProperty("text", out var rtext) || string.IsNullOrEmpty(rtext.GetString()))
                        errors.Add($"chunk {chunkId} of type risk must have non-empty text");
                    else if (rtext.GetString()!.Length > _options.ChunkTextMaxLength)
                        errors.Add($"chunk {chunkId} text exceeds max length of {_options.ChunkTextMaxLength}");
                    break;
                case "pattern":
                    if (!chunk.TryGetProperty("text", out var ptext) || string.IsNullOrEmpty(ptext.GetString()))
                        errors.Add($"chunk {chunkId} of type pattern must have non-empty text");
                    else if (ptext.GetString()!.Length > _options.ChunkTextMaxLength)
                        errors.Add($"chunk {chunkId} text exceeds max length of {_options.ChunkTextMaxLength}");
                    else if (ContainsPurityViolation(ptext.GetString()!, "pattern"))
                        errors.Add($"chunk {chunkId} pattern text contains constraint/feature language");
                    break;
                case "similar_case":
                    hasSimilarCaseChunk = true;
                    if (!chunk.TryGetProperty("task_shape", out var taskShape) || taskShape.ValueKind != JsonValueKind.Object)
                        errors.Add($"chunk {chunkId} of type similar_case must have task_shape object");
                    else
                    {
                        if (!taskShape.TryGetProperty("task_type", out var stt) || string.IsNullOrEmpty(stt.GetString()))
                            errors.Add($"chunk {chunkId} task_shape must have task_type");
                        if (!taskShape.TryGetProperty("feature_shape", out var sfs) || string.IsNullOrEmpty(sfs.GetString()))
                            errors.Add($"chunk {chunkId} task_shape must have feature_shape");
                        if (!taskShape.TryGetProperty("engine_change_allowed", out var eca) || (eca.ValueKind != JsonValueKind.True && eca.ValueKind != JsonValueKind.False))
                            errors.Add($"chunk {chunkId} task_shape must have engine_change_allowed boolean");
                        if (!taskShape.TryGetProperty("likely_layers", out var ll) || ll.ValueKind != JsonValueKind.Array)
                            errors.Add($"chunk {chunkId} task_shape must have likely_layers array");
                        if (!taskShape.TryGetProperty("risk_signals", out var rs) || rs.ValueKind != JsonValueKind.Array)
                            errors.Add($"chunk {chunkId} task_shape must have risk_signals array");
                    }
                    break;
            }
        }

        if (!hasCoreTaskChunk)
            errors.Add("at least one core_task chunk is required");

        if (_options.RequireConstraintChunk && hasHardConstraints && !hasConstraintChunk)
            errors.Add("constraint chunk required when hard_constraints is non-empty");

        if (_options.RequireRiskChunk && hasRiskSignals && !hasRiskChunk)
            errors.Add("risk chunk required when risk_signals is non-empty");

        if ((complexityLevel == "medium" || complexityLevel == "high") && !hasSimilarCaseChunk)
            errors.Add("similar_case chunk required for medium or high complexity");

        if (element.TryGetProperty("scopes", out var scopes) && scopes.ValueKind == JsonValueKind.Object)
        {
            var allowedScopeFields = new[] { "domain", "module", "layers", "concerns", "repos", "services", "symbols" };
            foreach (var prop in scopes.EnumerateObject())
            {
                if (!allowedScopeFields.Contains(prop.Name))
                    errors.Add($"unknown scope field: {prop.Name}");
                else if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                        if (item.ValueKind != JsonValueKind.String)
                            errors.Add($"scope.{prop.Name} must contain only strings");
                }
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static bool ContainsPurityViolation(string text, string chunkType)
    {
        var lower = text.ToLowerInvariant();
        return chunkType switch
        {
            "constraint" => lower.Contains("loading") || lower.Contains("ajax") || lower.Contains("full reload") || lower.Contains("re-render") || lower.Contains("rerender"),
            "pattern" => lower.Contains("must not") || lower.Contains("do not change") || lower.Contains("do not modify") || lower.Contains("cannot change") || lower.Contains("never"),
            _ => false
        };
    }
}