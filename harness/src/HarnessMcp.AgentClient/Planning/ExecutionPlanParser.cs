using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Planning;

public static class ExecutionPlanParser
{
    public static ExecutionPlan Parse(string modelJson)
    {
        if (!JsonHelpers.TryGetJsonObject(modelJson, out _))
            throw new InvalidOperationException("ExecutionPlan model output must be a JSON object.");

        var plan = JsonHelpers.Deserialize<ExecutionPlan>(modelJson);
        return plan;
    }
}

