namespace HarnessMcp.AgentClient.Config;

public sealed class HarnessRuntimeOptions
{
    public const string SectionName = "HarnessRuntime";

    public McpOptions Mcp { get; set; } = new();
    public ModelOptions Model { get; set; } = new();
    public PlanningDefaultsOptions PlanningDefaults { get; set; } = new();
    public PathsOptions Paths { get; set; } = new();
}

public sealed class McpOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class ModelOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string ApiKeyEnvVar { get; set; } = "OPENAI_API_KEY";
    public int TimeoutSeconds { get; set; } = 180;
}

public sealed class PlanningDefaultsOptions
{
    public string MinimumAuthority { get; set; } = "Reviewed";
    public int MaxItemsPerChunk { get; set; } = 5;
    public bool EmitIntermediates { get; set; } = true;
    public bool StdoutJson { get; set; } = true;
    public bool PrintWorkerPacket { get; set; } = false;
}

public sealed class PathsOptions
{
    public string DefaultOutputRoot { get; set; } = ".harness/runs";
}