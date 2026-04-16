using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarnessMcp.ControlPlane.Validators;

public class ExecutionPlanValidator
{
    private readonly ValidationOptions _options;

    public ExecutionPlanValidator(ValidationOptions options)
    {
        _options = options;
    }

    public ValidationResult Validate(object? value, object? requirementIntent)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (value is not JsonElement element)
        {
            return new ValidationResult { IsValid = false, Errors = new() { "ExecutionPlan must be a JSON object" } };
        }

        // Canonical required fields per design
        if (!element.TryGetProperty("task_id", out var taskId) || string.IsNullOrEmpty(taskId.GetString()))
            errors.Add("task_id is required and must be non-empty");

        if (!element.TryGetProperty("task", out var task) || string.IsNullOrEmpty(task.GetString()))
            errors.Add("task is required and must be non-empty");

        if (!element.TryGetProperty("scope", out var scopeElement) || string.IsNullOrEmpty(scopeElement.GetString()))
            errors.Add("scope is required and must be non-empty");

        if (!element.TryGetProperty("deliverables", out var deliverables) || deliverables.ValueKind != JsonValueKind.Array || deliverables.GetArrayLength() == 0)
            errors.Add("deliverables array is required and must not be empty");

        if (!element.TryGetProperty("forbidden_actions", out var forbiddenActions) || forbiddenActions.ValueKind != JsonValueKind.Array || forbiddenActions.GetArrayLength() == 0)
            errors.Add("forbidden_actions array is required and must not be empty");

        if (!element.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
        {
            errors.Add("steps array is required");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        var steps = stepsElement.EnumerateArray().ToList();
        if (steps.Count == 0)
        {
            errors.Add("at least one step is required");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        if (steps.Count > _options.MaxPlanSteps)
            warnings.Add($"step count {steps.Count} exceeds recommended max {_options.MaxPlanSteps}");

        // Constraints: at least one constraint is always required
        var hardConstraints = new List<string>();

        if (requirementIntent is JsonElement riElement && riElement.TryGetProperty("hard_constraints", out var hcElement))
        {
            foreach (var hc in hcElement.EnumerateArray())
            {
                var hcStr = hc.GetString();
                if (!string.IsNullOrEmpty(hcStr))
                    hardConstraints.Add(hcStr.ToLowerInvariant());
            }
        }

        if (!element.TryGetProperty("constraints", out var planConstraints) || planConstraints.ValueKind != JsonValueKind.Array || planConstraints.GetArrayLength() == 0)
        {
            errors.Add("constraints array is required and must not be empty");
        }
        else if (hardConstraints.Count > 0)
        {
            var planConstraintStrings = planConstraints.EnumerateArray()
                .Select(c => c.GetString()?.ToLowerInvariant())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            foreach (var hc in hardConstraints)
            {
                if (!planConstraintStrings.Any(pc => pc == hc || pc!.Contains(hc) || hc.Contains(pc!)))
                {
                    errors.Add($"hard constraint '{hc}' must be preserved in plan constraints");
                }
            }
        }

        var stepNumbers = new HashSet<int>();

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            if (!step.TryGetProperty("step_number", out var snElement))
            {
                errors.Add($"step {i} missing step_number");
                continue;
            }
            var stepNumber = snElement.GetInt32();
            if (!stepNumbers.Add(stepNumber))
                errors.Add($"duplicate step_number: {stepNumber}");

            if (!step.TryGetProperty("title", out var title) || string.IsNullOrEmpty(title.GetString()))
                errors.Add($"step {stepNumber} must have title");

            if (!step.TryGetProperty("actions", out var actionsElement) || actionsElement.ValueKind != JsonValueKind.Array || actionsElement.GetArrayLength() == 0)
                errors.Add($"step {stepNumber} must have at least one action");

            if (actionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in actionsElement.EnumerateArray())
                    if (action.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(action.GetString()))
                        errors.Add($"step {stepNumber} actions must be non-empty strings");
            }

            if (!step.TryGetProperty("outputs", out var outputsElement) || outputsElement.ValueKind != JsonValueKind.Array || outputsElement.GetArrayLength() == 0)
                errors.Add($"step {stepNumber} must have at least one output");

            if (outputsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var output in outputsElement.EnumerateArray())
                    if (output.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(output.GetString()))
                        errors.Add($"step {stepNumber} outputs must be non-empty strings");
            }

            if (!step.TryGetProperty("acceptance_checks", out var ac))
                errors.Add($"step {stepNumber} missing acceptance_checks");

            if (ac.ValueKind == JsonValueKind.Array && ac.GetArrayLength() == 0)
                errors.Add($"step {stepNumber} must have at least one acceptance_check");

            if (ac.ValueKind == JsonValueKind.Array)
            {
                foreach (var check in ac.EnumerateArray())
                    if (check.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(check.GetString()))
                        errors.Add($"step {stepNumber} acceptance_checks must be non-empty strings");
            }

            // Detect worker-side memory retrieval instructions
            var stepText = step.GetRawText().ToLowerInvariant();
            if (stepText.Contains("retrieve") && (stepText.Contains("memory") || stepText.Contains("mcp")))
            {
                if (stepText.Contains("execution") || stepText.Contains("worker") || stepText.Contains("during") || stepText.Contains("later"))
                    errors.Add($"step {stepNumber} instructs worker-side memory retrieval - forbidden in planning phase");
            }

            if (stepText.Contains("reinterpret") || stepText.Contains("change architecture") || stepText.Contains("modify architecture") || stepText.Contains("re-architect"))
            {
                errors.Add($"step {stepNumber} instructs architecture reinterpretation - forbidden");
            }
        }

        var expectedNumbers = Enumerable.Range(1, steps.Count).ToHashSet();
        if (!stepNumbers.SetEquals(expectedNumbers))
        {
            var missing = expectedNumbers.Except(stepNumbers);
            var extra = stepNumbers.Except(expectedNumbers);
            if (missing.Any())
                errors.Add($"missing step numbers: {string.Join(", ", missing)}");
            if (extra.Any())
                errors.Add($"non-consecutive step numbers: {string.Join(", ", extra)}");
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }
}
