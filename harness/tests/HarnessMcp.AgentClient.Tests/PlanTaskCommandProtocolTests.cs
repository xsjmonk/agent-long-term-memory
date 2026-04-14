using System.Text;
using System.Text.Json;
using System.IO;
using System;
using HarnessMcp.Contracts;
using FluentAssertions;
using HarnessMcp.AgentClient.Artifacts;
using HarnessMcp.AgentClient.Cli;
using HarnessMcp.AgentClient.Config;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.AgentClient.Support;
using HarnessMcp.AgentClient.Transport;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class PlanTaskCommandProtocolTests
{
    [Fact]
    public async Task plan_task_stdout_json_outputs_single_manifest_object_on_success()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "harness-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        var requirementIntentJson = JsonSerializer.Serialize(new
        {
            taskType = "core-task",
            domain = (string?)null,
            module = (string?)null,
            feature = (string?)null,
            goal = "do it",
            requestedOperations = new[] { "ajax refresh" },
            hardConstraints = new[] { "engine logic must not change" },
            softConstraints = Array.Empty<string>(),
            riskSignals = Array.Empty<string>(),
            candidateLayers = Array.Empty<string>(),
            retrievalFocuses = new[] { "placement" },
            ambiguities = Array.Empty<string>(),
            complexity = "low"
        });

        var planningModel = new RequirementAndPlanEchoModelClient(requirementIntentJson, objective: "objective");
        var mcp = new FakeMcpToolClient();

        var opts = new AgentClientOptions(
            TaskFile: null,
            TaskText: "RAW_TASK",
            OutputDir: outDir,
            McpBaseUrl: "http://example/mcp",
            ModelBaseUrl: "http://example",
            ModelName: "fake",
            ApiKeyEnv: "OPENAI_API_KEY",
            SessionId: null,
            Project: null,
            Domain: null,
            MaxItemsPerChunk: 5,
            MinimumAuthority: AuthorityLevel.Reviewed,
            EmitIntermediates: false,
            StdoutJson: true,
            PrintWorkerPacket: false);

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var artifactWriter = new PlanningArtifactWriter(new ArtifactPathBuilder(), emitIntermediates: false);

        var exit = await PlanTaskCommand.RunAsyncWithClients(
            opts,
            rawTask: "RAW_TASK",
            planningModelClient: planningModel,
            mcpToolClient: mcp,
            artifactWriter: artifactWriter,
            stdout: stdout,
            stderr: stderr,
            cancellationToken: CancellationToken.None);

        exit.Should().Be(0);

        stdout.ToString().Trim().Should().StartWith("{").And.EndWith("}");

        using var doc = JsonDocument.Parse(stdout.ToString());
        doc.RootElement.GetProperty("protocolName").GetString().Should().Be("HarnessMcp.AgentClient.PlanTaskProtocol");
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("nextAction").GetString().Should().Be("paste_worker_packet_into_execution_agent");
    }

    [Fact]
    public async Task plan_task_manifest_contains_next_action_and_paths()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "harness-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        var requirementIntentJson = JsonSerializer.Serialize(new
        {
            taskType = "core-task",
            domain = (string?)null,
            module = (string?)null,
            feature = (string?)null,
            goal = "do it",
            requestedOperations = new[] { "ajax refresh" },
            hardConstraints = new[] { "engine logic must not change" },
            softConstraints = Array.Empty<string>(),
            riskSignals = Array.Empty<string>(),
            candidateLayers = Array.Empty<string>(),
            retrievalFocuses = new[] { "placement" },
            ambiguities = Array.Empty<string>(),
            complexity = "low"
        });

        var planningModel = new RequirementAndPlanEchoModelClient(requirementIntentJson, objective: "objective");
        var mcp = new FakeMcpToolClient();

        var opts = new AgentClientOptions(
            TaskFile: null,
            TaskText: "RAW_TASK",
            OutputDir: outDir,
            McpBaseUrl: "http://example/mcp",
            ModelBaseUrl: "http://example",
            ModelName: "fake",
            ApiKeyEnv: "OPENAI_API_KEY",
            SessionId: null,
            Project: null,
            Domain: null,
            MaxItemsPerChunk: 5,
            MinimumAuthority: AuthorityLevel.Reviewed,
            EmitIntermediates: false,
            StdoutJson: true,
            PrintWorkerPacket: false);

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var artifactWriter = new PlanningArtifactWriter(new ArtifactPathBuilder(), emitIntermediates: false);

        var exit = await PlanTaskCommand.RunAsyncWithClients(
            opts,
            rawTask: "RAW_TASK",
            planningModelClient: planningModel,
            mcpToolClient: mcp,
            artifactWriter: artifactWriter,
            stdout: stdout,
            stderr: stderr,
            cancellationToken: CancellationToken.None);

        exit.Should().Be(0);

        var paths = new ArtifactPathBuilder();
        var manifestPath = paths.HarnessRunManifestJson(outDir);
        File.Exists(manifestPath).Should().BeTrue();

        var manifestJson = File.ReadAllText(manifestPath);
        using var doc = JsonDocument.Parse(manifestJson);

        doc.RootElement.GetProperty("nextAction").GetString().Should().Be("paste_worker_packet_into_execution_agent");

        doc.RootElement.GetProperty("sessionJsonPath").GetString().Should().Be(paths.SessionJson(outDir));
        doc.RootElement.GetProperty("executionPlanMarkdownPath").GetString().Should().Be(paths.ExecutionPlanMd(outDir));
        doc.RootElement.GetProperty("workerPacketMarkdownPath").GetString().Should().Be(paths.WorkerPacketMd(outDir));
    }

    private sealed class RequirementAndPlanEchoModelClient : IPlanningModelClient
    {
        private readonly string _requirementIntentJson;
        private readonly string _objective;
        private int _callCount;

        public RequirementAndPlanEchoModelClient(string requirementIntentJson, string objective)
        {
            _requirementIntentJson = requirementIntentJson;
            _objective = objective;
        }

        public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            _callCount++;
            if (_callCount == 1)
                return Task.FromResult(_requirementIntentJson);

            var sessionId = Extract(userPrompt, "sessionId");
            var taskId = Extract(userPrompt, "taskId");

            // Keep plan valid: must include hard constraints and acceptance checks.
            var plan = new
            {
                sessionId = sessionId,
                taskId = taskId,
                objective = _objective,
                assumptions = Array.Empty<string>(),
                hardConstraints = new[] { "engine logic must not change" },
                antiPatternsToAvoid = Array.Empty<string>(),
                steps = new[]
                {
                    new
                    {
                        stepNumber = 1,
                        title = "Step 1",
                        purpose = "Do it",
                        inputs = Array.Empty<string>(),
                        actions = new[] { "implement changes" },
                        outputs = new[] { "code updated" },
                        acceptanceChecks = new[] { "passes" },
                        supportingMemoryIds = Array.Empty<string>(),
                        notes = Array.Empty<string>()
                    }
                },
                validationChecks = Array.Empty<string>(),
                deliverables = new[] { "done" },
                openQuestions = Array.Empty<string>()
            };

            return Task.FromResult(JsonHelpers.Serialize(plan));
        }

        private static string Extract(string userPrompt, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                userPrompt,
                $"\"{key}\"\\s*:\\s*\"(?<v>[^\"]*)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!m.Success) throw new InvalidOperationException("Missing key " + key + " in prompt.");
            return m.Groups["v"].Value;
        }
    }
}

