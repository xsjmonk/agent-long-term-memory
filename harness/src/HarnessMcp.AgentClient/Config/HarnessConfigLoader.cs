using System.IO;
using System.Text.Json;
using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Config;

public static class HarnessConfigLoader
{
    private const string ConfigFileName = "appsettings.harness.json";

    public static RunResult<AgentClientOptions> LoadWithConfig(string[] args)
    {
        var basePath = FindConfigBasePath();
        var configFilePath = basePath is null ? null : Path.Combine(basePath, ConfigFileName);

        HarnessRuntimeOptions? fromFile = null;
        if (configFilePath is not null && File.Exists(configFilePath))
        {
            var json = File.ReadAllText(configFilePath);
            fromFile = JsonSerializer.Deserialize<HarnessRuntimeOptions>(json, JsonHelpers.Default);
        }

        var resolved = Resolve(args, fromFile);
        return resolved;
    }

    private static string? FindConfigBasePath()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var configPath = Path.Combine(current.FullName, ConfigFileName);
            if (File.Exists(configPath))
                return current.FullName;

            var harnessDir = Path.Combine(current.FullName, "harness");
            if (Directory.Exists(harnessDir))
            {
                var inHarness = Path.Combine(harnessDir, ConfigFileName);
                if (File.Exists(inHarness))
                    return harnessDir;
            }

            current = current.Parent;
        }

        return null;
    }

    private static RunResult<AgentClientOptions> Resolve(string[] args, HarnessRuntimeOptions? fromFile)
    {
        string? taskFile = null;
        string? taskText = null;
        string? outputDir = null;
        string? mcpBaseUrl = null;
        string? modelBaseUrl = null;
        string? modelName = null;
        string? apiKeyEnv = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? NextVal() => i + 1 < args.Length ? args[i + 1] : null;

            switch (a)
            {
                case "--task-file": taskFile = NextVal(); i++; break;
                case "--task-text": taskText = NextVal(); i++; break;
                case "--output-dir": outputDir = NextVal(); i++; break;
                case "--mcp-base-url": mcpBaseUrl = NextVal(); i++; break;
                case "--model-base-url": modelBaseUrl = NextVal(); i++; break;
                case "--model-name": modelName = NextVal(); i++; break;
                case "--api-key-env": apiKeyEnv = NextVal(); i++; break;
            }
        }

        mcpBaseUrl ??= Environment.GetEnvironmentVariable("HARNESS_MCP_BASE_URL") ?? fromFile?.Mcp.BaseUrl;
        modelBaseUrl ??= Environment.GetEnvironmentVariable("HARNESS_MODEL_BASE_URL") ?? fromFile?.Model.BaseUrl;
        modelName ??= Environment.GetEnvironmentVariable("HARNESS_MODEL_NAME") ?? fromFile?.Model.ModelName;
        apiKeyEnv ??= Environment.GetEnvironmentVariable("HARNESS_MODEL_API_KEY_ENV") ?? fromFile?.Model.ApiKeyEnvVar ?? "OPENAI_API_KEY";

        if (string.IsNullOrWhiteSpace(mcpBaseUrl))
            return RunResult.Failure<AgentClientOptions>("MCP base URL is required. Set HARNESS_MCP_BASE_URL, use --mcp-base-url, or configure in appsettings.harness.json.");
        if (string.IsNullOrWhiteSpace(modelBaseUrl))
            return RunResult.Failure<AgentClientOptions>("Model base URL is required. Set HARNESS_MODEL_BASE_URL, use --model-base-url, or configure in appsettings.harness.json.");
        if (string.IsNullOrWhiteSpace(modelName))
            return RunResult.Failure<AgentClientOptions>("Model name is required. Set HARNESS_MODEL_NAME, use --model-name, or configure in appsettings.harness.json.");

        outputDir ??= Environment.GetEnvironmentVariable("HARNESS_OUTPUT_ROOT") ?? fromFile?.Paths.DefaultOutputRoot ?? ".harness/runs";

        if (taskFile is null && taskText is null)
            return RunResult.Failure<AgentClientOptions>("One of --task-file or --task-text is required.");

        var taskInputs = (taskFile is not null ? 1 : 0) + (taskText is not null ? 1 : 0);
        if (taskInputs != 1)
            return RunResult.Failure<AgentClientOptions>("Exactly one of --task-file or --task-text must be provided.");

        if (taskFile is not null && !File.Exists(taskFile))
            return RunResult.Failure<AgentClientOptions>($"Task file not found: {taskFile}");

        var minAuthority = fromFile?.PlanningDefaults.MinimumAuthority ?? "Reviewed";
        var maxItems = fromFile?.PlanningDefaults.MaxItemsPerChunk ?? 5;
        var emitIntermediates = fromFile?.PlanningDefaults.EmitIntermediates ?? true;
        var stdoutJson = fromFile?.PlanningDefaults.StdoutJson ?? true;
        var printWorkerPacket = fromFile?.PlanningDefaults.PrintWorkerPacket ?? false;

        if (!Enum.TryParse<Contracts.AuthorityLevel>(minAuthority, ignoreCase: true, out var authority))
            return RunResult.Failure<AgentClientOptions>($"Invalid MinimumAuthority: {minAuthority}");

        var opts = new AgentClientOptions(
            TaskFile: taskFile,
            TaskText: taskText,
            OutputDir: outputDir,
            McpBaseUrl: mcpBaseUrl,
            ModelBaseUrl: modelBaseUrl,
            ModelName: modelName,
            ApiKeyEnv: apiKeyEnv,
            SessionId: null,
            Project: null,
            Domain: null,
            MaxItemsPerChunk: maxItems,
            MinimumAuthority: authority,
            EmitIntermediates: emitIntermediates,
            StdoutJson: stdoutJson,
            PrintWorkerPacket: printWorkerPacket);

        return RunResult.Success(opts);
    }
}