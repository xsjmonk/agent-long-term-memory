using HarnessMcp.Contracts;
using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Config;

public static class AgentClientOptionsLoader
{
    public static RunResult<AgentClientOptions> Load(string[] args)
    {
        string? taskFile = null;
        string? taskText = null;
        string? outputDir = null;
        string? mcpBaseUrl = null;
        string? modelBaseUrl = null;
        string? modelName = null;
        string apiKeyEnv = "OPENAI_API_KEY";
        string? sessionId = null;
        string? project = null;
        string? domain = null;
        int maxItemsPerChunk = 5;
        AuthorityLevel minimumAuthority = AuthorityLevel.Reviewed;
        bool emitIntermediates = true;
        bool stdoutJson = true;
        bool printWorkerPacket = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? NextVal()
            {
                if (i + 1 >= args.Length)
                    return null;
                return args[i + 1];
            }

            string? v;
            switch (a)
            {
                case "--task-file":
                    v = NextVal();
                    if (v is null) return RunResult.Failure<AgentClientOptions>("Missing value for --task-file.");
                    taskFile = v;
                    i++;
                    break;
                case "--task-text":
                    v = NextVal();
                    if (v is null) return RunResult.Failure<AgentClientOptions>("Missing value for --task-text.");
                    taskText = v;
                    i++;
                    break;
                case "--output-dir":
                    v = NextVal();
                    if (v is null) return RunResult.Failure<AgentClientOptions>("Missing value for --output-dir.");
                    outputDir = v;
                    i++;
                    break;
                case "--mcp-base-url":
                    v = NextVal();
                    if (v is null) return RunResult.Failure<AgentClientOptions>("Missing value for --mcp-base-url.");
                    mcpBaseUrl = v;
                    i++;
                    break;
                case "--model-base-url":
                    v = NextVal();
                    if (v is null) return RunResult.Failure<AgentClientOptions>("Missing value for --model-base-url.");
                    modelBaseUrl = v;
                    i++;
                    break;
                case "--model-name":
                    v = NextVal();
                    if (v is null) return RunResult.Failure<AgentClientOptions>("Missing value for --model-name.");
                    modelName = v;
                    i++;
                    break;
                case "--api-key-env":
                    v = NextVal();
                    if (v is null) return RunResult.Failure<AgentClientOptions>("Missing value for --api-key-env.");
                    apiKeyEnv = v;
                    i++;
                    break;
                case "--session-id":
                    v = NextVal();
                    if (v is null) return RunResult.Failure<AgentClientOptions>("Missing value for --session-id.");
                    sessionId = v;
                    i++;
                    break;
                case "--project":
                    v = NextVal();
                    if (v is null) return RunResult.Failure<AgentClientOptions>("Missing value for --project.");
                    project = v;
                    i++;
                    break;
                case "--domain":
                    v = NextVal();
                    if (v is null) return RunResult.Failure<AgentClientOptions>("Missing value for --domain.");
                    domain = v;
                    i++;
                    break;
                case "--max-items-per-chunk":
                    v = NextVal();
                    if (v is null || !int.TryParse(v, out maxItemsPerChunk))
                        return RunResult.Failure<AgentClientOptions>("Invalid value for --max-items-per-chunk.");
                    i++;
                    break;
                case "--minimum-authority":
                    v = NextVal();
                    if (v is null || !Enum.TryParse<AuthorityLevel>(v, ignoreCase: true, out minimumAuthority))
                        return RunResult.Failure<AgentClientOptions>("Invalid value for --minimum-authority.");
                    i++;
                    break;
                case "--emit-intermediates":
                    v = NextVal();
                    if (v is null || !bool.TryParse(v, out emitIntermediates))
                        return RunResult.Failure<AgentClientOptions>("Invalid value for --emit-intermediates.");
                    i++;
                    break;
                case "--stdout-json":
                    v = NextVal();
                    if (v is null || !bool.TryParse(v, out stdoutJson))
                        return RunResult.Failure<AgentClientOptions>("Invalid value for --stdout-json.");
                    i++;
                    break;
                case "--print-worker-packet":
                    v = NextVal();
                    if (v is null || !bool.TryParse(v, out printWorkerPacket))
                        return RunResult.Failure<AgentClientOptions>("Invalid value for --print-worker-packet.");
                    i++;
                    break;
                default:
                    return RunResult.Failure<AgentClientOptions>($"Unknown argument: {a}");
            }
        }

        var taskInputs = (taskFile is not null ? 1 : 0) + (taskText is not null ? 1 : 0);
        if (taskInputs != 1)
            return RunResult.Failure<AgentClientOptions>("Exactly one of --task-file or --task-text must be provided.");

        if (string.IsNullOrWhiteSpace(outputDir)) return RunResult.Failure<AgentClientOptions>("Missing required argument: --output-dir");
        if (string.IsNullOrWhiteSpace(mcpBaseUrl)) return RunResult.Failure<AgentClientOptions>("Missing required argument: --mcp-base-url");
        if (string.IsNullOrWhiteSpace(modelBaseUrl)) return RunResult.Failure<AgentClientOptions>("Missing required argument: --model-base-url");
        if (string.IsNullOrWhiteSpace(modelName)) return RunResult.Failure<AgentClientOptions>("Missing required argument: --model-name");

        if (taskFile is not null && !File.Exists(taskFile))
            return RunResult.Failure<AgentClientOptions>($"Task file not found: {taskFile}");

        var opts = new AgentClientOptions(
            TaskFile: taskFile,
            TaskText: taskText,
            OutputDir: outputDir,
            McpBaseUrl: mcpBaseUrl,
            ModelBaseUrl: modelBaseUrl,
            ModelName: modelName,
            ApiKeyEnv: apiKeyEnv,
            SessionId: sessionId,
            Project: project,
            Domain: domain,
            MaxItemsPerChunk: maxItemsPerChunk,
            MinimumAuthority: minimumAuthority,
            EmitIntermediates: emitIntermediates,
            StdoutJson: stdoutJson,
            PrintWorkerPacket: printWorkerPacket);

        return RunResult.Success(opts);
    }
}

