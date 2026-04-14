using HarnessMcp.AgentClient.Planning;
using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Cli;

public static class DescribeProtocolCommand
{
    public static string GetProtocolJson()
    {
        var desc = new HarnessProtocolDescription(
            ProtocolName: "HarnessMcp.AgentClient.PlanTaskProtocol",
            ProtocolVersion: "1.0",
            PrimaryCommand: "plan-task",
            RequiredArguments: new[]
            {
                new ProtocolArgument("--task-file", Required: true, Description: "Path to a text file containing the raw task."),
                new ProtocolArgument("--task-text", Required: true, Description: "Raw task text (exactly one of --task-file or --task-text must be provided)."),
                new ProtocolArgument("--output-dir", Required: true, Description: "Directory to write harness artifacts."),
                new ProtocolArgument("--mcp-base-url", Required: true, Description: "Base URL of the running MCP server (HTTP MCP only)."),
                new ProtocolArgument("--model-base-url", Required: true, Description: "Base URL for OpenAI-compatible planning model HTTP endpoint."),
                new ProtocolArgument("--model-name", Required: true, Description: "Model name for the OpenAI-compatible planning endpoint.")
            },
            OptionalArguments: new[]
            {
                new ProtocolArgument("--api-key-env", Required: false, Description: "Environment variable name containing the API key. Default: OPENAI_API_KEY."),
                new ProtocolArgument("--session-id", Required: false, Description: "Optional session id (string)."),
                new ProtocolArgument("--project", Required: false, Description: "Optional project metadata forwarded to requirement interpretation."),
                new ProtocolArgument("--domain", Required: false, Description: "Optional domain metadata forwarded to requirement interpretation."),
                new ProtocolArgument("--max-items-per-chunk", Required: false, Description: "Max items per retrieval chunk. Default: 5."),
                new ProtocolArgument("--minimum-authority", Required: false, Description: "Minimum authority level. Default: Reviewed."),
                new ProtocolArgument("--emit-intermediates", Required: false, Description: "Whether to emit intermediate artifacts. Default: true."),
                new ProtocolArgument("--stdout-json", Required: false, Description: "When true, print only the 12-harness-run-manifest.json JSON object to stdout. Default: true."),
                new ProtocolArgument("--print-worker-packet", Required: false, Description: "When true, include the full worker packet markdown in the run manifest. Default: false.")
            },
            OutputArtifacts: new[]
            {
                "00-session.json",
                "01-raw-task.txt",
                "02-requirement-intent.json",
                "03-retrieval-chunks.json",
                "04-chunk-quality-report.json",
                "05-retrieve-memory-by-chunks.json",
                "06-merge-retrieval-results.json",
                "07-build-memory-context-pack.json",
                "08-planning-memory-summary.md",
                "09-execution-plan.json",
                "10-execution-plan.md",
                "11-worker-packet.md",
                "12-harness-run-manifest.json"
            },
            StdoutFields: new[]
            {
                "HarnessRunManifest"
            },
            RequiredAgentBehavior: new[]
            {
                "call plan-task before execution work begins",
                "use the machine-readable result manifest instead of guessing artifact names",
                "use the worker packet produced by the harness as the execution handoff",
                "follow nextAction exactly as specified in the manifest",
                "stop and fix errors when harness returns success=false"
            },
            ForbiddenAgentBehavior: new[]
            {
                "do not skip harness planning for non-trivial tasks",
                "do not begin coding or direct planning before harness success",
                "do not retrieve long-term memory independently outside the harness flow",
                "do not generate a replacement plan outside the harness",
                "do not expand scope beyond the worker packet steps",
                "do not reinterpret the task at the architecture level during execution"
            },
            SuccessExitMeaning: "Exit code 0 and manifest.Success=true",
            FailureExitMeaning: "Exit code non-zero and manifest.Success=false",
            NextActionOnSuccess: "paste_worker_packet_into_execution_agent");

        return JsonHelpers.Serialize(desc, JsonHelpers.Default);
    }
}

