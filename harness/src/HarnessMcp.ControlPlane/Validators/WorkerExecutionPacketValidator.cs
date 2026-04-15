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

        // Canonical required fields per design
        if (!element.TryGetProperty("goal", out var goal) || string.IsNullOrEmpty(goal.GetString()))
            errors.Add("goal is required and must be non-empty");

        if (!element.TryGetProperty("scope", out var scope) || string.IsNullOrEmpty(scope.GetString()))
            errors.Add("scope is required and must be non-empty");

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

        // Check steps for forbidden instructions (not execution_rules, which may contain prohibitions)
        if (element.TryGetProperty("steps", out var stepsCheck) && stepsCheck.ValueKind == JsonValueKind.Array)
        {
            var stepsText = stepsCheck.GetRawText().ToLowerInvariant();

            // Detect if steps INSTRUCT memory retrieval (not prohibit it — prohibitions are in execution_rules)
            // Only flag if "retrieve" appears alongside "memory" without a clear negation marker
            if (stepsText.Contains("retrieve") &&
                (stepsText.Contains("memory") || stepsText.Contains("long_term") || stepsText.Contains("longterm")))
            {
                // Only error if the text appears to instruct retrieval (not prohibit it)
                var hasNegation = stepsText.Contains("do not retrieve") || stepsText.Contains("don't retrieve") ||
                                  stepsText.Contains("no retrieve") || stepsText.Contains("forbid") || stepsText.Contains("prohibit");
                if (!hasNegation)
                    errors.Add("steps must not instruct worker-side independent memory retrieval");
            }

            if (stepsText.Contains("replan") || stepsText.Contains("re-plan") || stepsText.Contains("replace plan") || stepsText.Contains("regenerate plan"))
            {
                errors.Add("steps must not contain replacement-plan instructions");
            }

            if (stepsText.Contains("reinterpret") || stepsText.Contains("re-interpret") || stepsText.Contains("change architecture") || stepsText.Contains("modify architecture"))
            {
                errors.Add("steps must not contain reinterpret-architecture instructions");
            }
        }

        // Verify hard constraints are preserved from execution plan
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
