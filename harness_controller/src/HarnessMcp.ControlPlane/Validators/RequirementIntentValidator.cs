using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarnessMcp.ControlPlane.Validators;

public class RequirementIntentValidator
{
    public ValidationResult Validate(object? value)
    {
        var errors = new List<string>();
        
        if (value is not JsonElement element)
        {
            return new ValidationResult { IsValid = false, Errors = new() { "RequirementIntent must be a JSON object" } };
        }

        if (!element.TryGetProperty("task_id", out var taskId) || string.IsNullOrEmpty(taskId.GetString()))
            errors.Add("task_id is required and must be a non-empty string");

        if (!element.TryGetProperty("task_type", out var taskType) || string.IsNullOrEmpty(taskType.GetString()))
            errors.Add("task_type is required and must be a non-empty string");

        if (!element.TryGetProperty("goal", out var goal) || string.IsNullOrEmpty(goal.GetString()))
            errors.Add("goal is required and must be a non-empty string");

        if (!element.TryGetProperty("hard_constraints", out var hc))
            errors.Add("hard_constraints array is required");
        else if (hc.ValueKind != JsonValueKind.Array)
            errors.Add("hard_constraints must be an array");
        else
            foreach (var item in hc.EnumerateArray())
                if (item.ValueKind != JsonValueKind.String)
                    errors.Add("hard_constraints array must contain only strings");

        if (!element.TryGetProperty("risk_signals", out var rs))
            errors.Add("risk_signals array is required");
        else if (rs.ValueKind != JsonValueKind.Array)
            errors.Add("risk_signals must be an array");
        else
            foreach (var item in rs.EnumerateArray())
                if (item.ValueKind != JsonValueKind.String)
                    errors.Add("risk_signals array must contain only strings");

        if (!element.TryGetProperty("complexity", out var complexity) || string.IsNullOrEmpty(complexity.GetString()))
            errors.Add("complexity is required (low|medium|high)");
        else
        {
            var complexityValue = complexity.GetString();
            if (complexityValue != "low" && complexityValue != "medium" && complexityValue != "high")
                errors.Add("complexity must be one of: low, medium, high");
        }

        if (element.TryGetProperty("requested_operations", out var ro) && ro.ValueKind == JsonValueKind.Array)
            foreach (var item in ro.EnumerateArray())
                if (item.ValueKind != JsonValueKind.String)
                    errors.Add("requested_operations array must contain only strings");

        if (element.TryGetProperty("soft_constraints", out var sc) && sc.ValueKind == JsonValueKind.Array)
            foreach (var item in sc.EnumerateArray())
                if (item.ValueKind != JsonValueKind.String)
                    errors.Add("soft_constraints array must contain only strings");

        if (element.TryGetProperty("candidate_layers", out var cl) && cl.ValueKind == JsonValueKind.Array)
            foreach (var item in cl.EnumerateArray())
                if (item.ValueKind != JsonValueKind.String)
                    errors.Add("candidate_layers array must contain only strings");

        if (element.TryGetProperty("retrieval_focuses", out var rf) && rf.ValueKind == JsonValueKind.Array)
            foreach (var item in rf.EnumerateArray())
                if (item.ValueKind != JsonValueKind.String)
                    errors.Add("retrieval_focuses array must contain only strings");

        if (element.TryGetProperty("ambiguities", out var amb) && amb.ValueKind == JsonValueKind.Array)
            foreach (var item in amb.EnumerateArray())
                if (item.ValueKind != JsonValueKind.String)
                    errors.Add("ambiguities array must contain only strings");

        var presentProperties = new HashSet<string>();
        foreach (var prop in element.EnumerateObject())
            presentProperties.Add(prop.Name);

        var canonicalRequired = new HashSet<string> { "task_id", "task_type", "goal", "hard_constraints", "risk_signals", "complexity" };
        var unknownFields = presentProperties.Except(canonicalRequired)
            .Except(new[] { "domain", "module", "feature", "requested_operations", "soft_constraints", "candidate_layers", "retrieval_focuses", "ambiguities" });
        if (unknownFields.Any())
            errors.Add($"unknown fields: {string.Join(", ", unknownFields)}");

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }
}

public class ValidationResult
{
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}