using HarnessMcp.AgentClient.Config;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.AgentClient.Transport;
using HarnessMcp.AgentClient.Artifacts;
using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Cli;

public static class PlanTaskCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var parsed = AgentClientOptionsLoader.Load(args);
        if (!parsed.IsSuccess)
        {
            foreach (var e in parsed.Errors) Console.Error.WriteLine(e);
            return 1;
        }

        var opts = parsed.Value!;
        Directory.CreateDirectory(opts.OutputDir);

        var apiKey = Environment.GetEnvironmentVariable(opts.ApiKeyEnv);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine($"Missing API key env var: {opts.ApiKeyEnv}");
            return 1;
        }

        try
        {
            var rawTask = opts.TaskText;
            if (rawTask is null)
            {
                rawTask = File.ReadAllText(opts.TaskFile!);
            }

            var artifactWriter = new PlanningArtifactWriter(
                new ArtifactPathBuilder(),
                emitIntermediates: opts.EmitIntermediates);

            // One planning-model client is reused for both passes.
            var modelClient = new OpenAiCompatiblePlanningModelClient(
                endpointBaseUrl: opts.ModelBaseUrl,
                modelName: opts.ModelName,
                apiKey: apiKey);

            var mcpClient = new HttpMcpToolClient(opts.McpBaseUrl);

            var runner = new PlanningSessionRunner(
                options: opts,
                planningModelClient: modelClient,
                mcpToolClient: mcpClient,
                artifactWriter: artifactWriter);

            var result = await runner.RunAsync(rawTask, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                foreach (var e in result.Errors) Console.Error.WriteLine(e);
                return 1;
            }

            Console.WriteLine("Execution plan: " + result.Value!.ExecutionPlanMarkdownPath);
            Console.WriteLine("Worker packet: " + result.Value!.WorkerPacketMarkdownPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Planning failed: " + ex);
            return 1;
        }
    }
}

