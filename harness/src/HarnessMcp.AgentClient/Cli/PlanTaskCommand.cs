using HarnessMcp.AgentClient.Config;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.AgentClient.Transport;
using HarnessMcp.AgentClient.Artifacts;
using HarnessMcp.AgentClient.Support;
using System.IO;

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

        var rawTask = opts.TaskText ?? File.ReadAllText(opts.TaskFile!);
        var artifactWriter = new PlanningArtifactWriter(
            new ArtifactPathBuilder(),
            emitIntermediates: opts.EmitIntermediates);

        // One planning-model client is reused for both passes.
        var modelClient = new OpenAiCompatiblePlanningModelClient(
            endpointBaseUrl: opts.ModelBaseUrl,
            modelName: opts.ModelName,
            apiKey: apiKey);

        var mcpClient = new HttpMcpToolClient(opts.McpBaseUrl);

        return await RunAsyncWithClients(
                opts,
                rawTask,
                planningModelClient: modelClient,
                mcpToolClient: mcpClient,
                artifactWriter: artifactWriter,
                stdout: Console.Out,
                stderr: Console.Error,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<int> RunAsyncWithClients(
        AgentClientOptions opts,
        string rawTask,
        IPlanningModelClient planningModelClient,
        IMcpToolClient mcpToolClient,
        PlanningArtifactWriter artifactWriter,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var paths = new ArtifactPathBuilder();
        var manifestPath = paths.HarnessRunManifestJson(opts.OutputDir);

        try
        {
            var runner = new PlanningSessionRunner(
                options: opts,
                planningModelClient: planningModelClient,
                mcpToolClient: mcpToolClient,
                artifactWriter: artifactWriter);

            var result = await runner.RunAsync(rawTask, cancellationToken).ConfigureAwait(false);

            if (opts.StdoutJson)
            {
                // Always print a single JSON object to stdout when requested.
                if (File.Exists(manifestPath))
                {
                    var manifestJson = File.ReadAllText(manifestPath);
                    stdout.WriteLine(manifestJson);
                }
                else
                {
                    // Manifest should always exist; best-effort fallback.
                    var fallback = JsonHelpers.Serialize(new
                    {
                        ProtocolName = "HarnessMcp.AgentClient.PlanTaskProtocol",
                        ProtocolVersion = "1.0",
                        Success = false,
                        Warnings = Array.Empty<string>(),
                        Errors = new[] { "Manifest not found after harness run." }
                    }, JsonHelpers.Default);
                    stdout.WriteLine(fallback);
                }

                return result.IsSuccess ? 0 : 1;
            }

            if (!result.IsSuccess)
            {
                foreach (var e in result.Errors) stderr.WriteLine(e);
                return 1;
            }

            stdout.WriteLine("Execution plan: " + result.Value!.ExecutionPlanMarkdownPath);
            stdout.WriteLine("Worker packet: " + result.Value!.WorkerPacketMarkdownPath);
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine("Planning failed: " + ex);

            if (opts.StdoutJson)
            {
                if (File.Exists(manifestPath))
                    stdout.WriteLine(File.ReadAllText(manifestPath));
            }

            return 1;
        }
    }
}

