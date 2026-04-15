using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarnessMcp.ControlPlane.Validators;

public class ChunkQualityReportValidator
{
    public ValidationResult Validate(object? value)
    {
        var errors = new List<string>();

        if (value is not JsonElement element)
        {
            return new ValidationResult { IsValid = false, Errors = new() { "ChunkQualityReport must be a JSON object" } };
        }

        if (!element.TryGetProperty("isValid", out var isValidElement))
        {
            errors.Add("isValid field is required");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        var isValid = isValidElement.GetBoolean();
        if (!isValid)
        {
            if (!element.TryGetProperty("errors", out var errsElement) || errsElement.ValueKind != JsonValueKind.Array || errsElement.GetArrayLength() == 0)
            {
                errors.Add("isValid=false requires non-empty errors array");
            }
            else
            {
                foreach (var err in errsElement.EnumerateArray())
                {
                    var errStr = err.GetString();
                    if (string.IsNullOrEmpty(errStr))
                        errors.Add("errors array must contain only non-empty strings");
                }
            }
            if (!element.TryGetProperty("warnings", out var warnsElement) || warnsElement.ValueKind != JsonValueKind.Array)
                errors.Add("warnings array is required");
        }
        else
        {
            if (!element.TryGetProperty("errors", out var errsElement) || errsElement.ValueKind != JsonValueKind.Array)
                errors.Add("errors array is required (can be empty for valid report)");
            if (!element.TryGetProperty("warnings", out var warnsElement) || warnsElement.ValueKind != JsonValueKind.Array)
                errors.Add("warnings array is required (can be empty for valid report)");
            if (!element.TryGetProperty("has_core_task", out _))
                errors.Add("has_core_task field is required");
            if (!element.TryGetProperty("has_constraint", out _))
                errors.Add("has_constraint field is required");
            if (!element.TryGetProperty("has_risk", out _))
                errors.Add("has_risk field is required");
            if (!element.TryGetProperty("has_pattern", out _))
                errors.Add("has_pattern field is required");
            if (!element.TryGetProperty("has_similar_case", out _))
                errors.Add("has_similar_case field is required");
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }
}