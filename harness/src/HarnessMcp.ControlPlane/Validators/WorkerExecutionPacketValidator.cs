using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarnessMcp.ControlPlane.Validators;

public class WorkerExecutionPacketValidator
{
    public ValidationResult Validate(object? value, object? executionPlan)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (value is not JsonElement element)
        {
            return new ValidationResult { IsValid = false, Errors = new() { "WorkerExecutionPacket must be a JSON object" } };
        }

        if (!element.TryGetProperty("objective", out var objective) || string.IsNullOrEmpty(objective.GetString()))
            errors.Add("objective is required and must be non-empty");

        if (!element.TryGetProperty("hard_constraints", out var hardConstraints) || hardConstraints.ValueKind != JsonValueKind.Array || hardConstraints.GetArrayLength() == 0)
            errors.Add("hard_constraints array is required and must not be empty");

        if (!element.TryGetProperty("forbidden_actions", out var forbiddenActions) || forbiddenActions.ValueKind != JsonValueKind.Array || forbiddenActions.GetArrayLength() == 0)
            errors.Add("forbidden_actions array is required and must not be empty");

        if (!element.TryGetProperty("execution_rules", out var executionRules) || executionRules.ValueKind != JsonValueKind.Array || executionRules.GetArrayLength() == 0)
        {
            errors.Add("execution_rules array is required and must not be empty");
        }
        else
        {
            var rulesText = executionRules.GetRawText().ToLowerInvariant();
            if (!rulesText.Contains("memory") || (!rulesText.Contains("forbid") && !rulesText.Contains("prohibit") && !rulesText.Contains("prevent") && !rulesText.Contains("no ") && !rulesText.Contains("don't") && !rulesText.Contains("do not")))
                errors.Add("execution_rules must explicitly prohibit memory retrieval");
        }

        if (!element.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() == 0)
            errors.Add("steps array is required and must not be empty");

        if (!element.TryGetProperty("required_output_sections", out var ros) || ros.ValueKind != JsonValueKind.Array || ros.GetArrayLength() == 0)
            errors.Add("required_output_sections array is required and must not be empty");

        var rawText = element.GetRawText().ToLowerInvariant();
        if (rawText.Contains("retrieve") && (rawText.Contains("memory") || rawText.Contains("long_term") || rawText.Contains("longterm")) && (rawText.Contains("execution") || rawText.Contains("worker") || rawText.Contains("phase") || rawText.Contains("during") || rawText.Contains("independently")))
        {
            errors.Add("worker packet must explicitly prohibit independent long-term-memory retrieval in execution phase");
        }

        if (rawText.Contains("replan") || rawText.Contains("re-plan") || rawText.Contains("replace plan") || rawText.Contains("regenerate plan"))
        {
            errors.Add("worker packet must forbid replacement-plan language");
        }

        if (rawText.Contains("reinterpret") || rawText.Contains("re-interpret") || rawText.Contains("change architecture") || rawText.Contains("modify architecture"))
        {
            errors.Add("worker packet must forbid reinterpret-architecture language");
        }

        if (executionPlan is JsonElement epElement)
        {
            if (epElement.TryGetProperty("constraints", out var planConstraints) && planConstraints.ValueKind == JsonValueKind.Array && planConstraints.GetArrayLength() > 0)
            {
                if (element.TryGetProperty("hard_constraints", out var packetConstraints) && packetConstraints.ValueKind == JsonValueKind.Array)
                {
                    var packetConstraintStrings = packetConstraints.EnumerateArray()
                        .Select(c => c.GetString()?.ToLowerInvariant())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();

                    foreach (var pc in planConstraints.EnumerateArray())
                    {
                        var pcStr = pc.GetString()?.ToLowerInvariant();
                        if (!string.IsNullOrEmpty(pcStr) && !packetConstraintStrings.Any(pcs => pcs == pcStr || pcs!.Contains(pcStr) || pcStr.Contains(pcs!)))
                        {
                            errors.Add($"hard constraint '{pcStr}' from execution plan must be preserved in worker packet");
                        }
                    }
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
}