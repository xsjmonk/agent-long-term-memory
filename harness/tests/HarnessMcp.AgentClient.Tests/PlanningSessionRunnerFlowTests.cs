using System.Text.Json;
using FluentAssertions;
using HarnessMcp.AgentClient.Artifacts;
using HarnessMcp.AgentClient.Config;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.AgentClient.Transport;
using HarnessMcp.Contracts;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class PlanningSessionRunnerFlowTests
{
    [Fact]
    public async Task mcp_tool_call_order_and_model_pass_order_are_strict()
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

        var mcp = new FakeMcpToolClient();

        // Use a planning model fake that ensures build_memory_context_pack has happened by pass2.
        var model = new OrderAssertingPlanningModelClient(
            requirementIntentJson,
            executionPlanHardConstraint: "engine logic must not change",
            mcpTool: mcp);

        var opts = new AgentClientOptions(
            TaskFile: null,
            TaskText: "RAW",
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
            StdoutJson: false,
            PrintWorkerPacket: false);

        var artifactWriter = new PlanningArtifactWriter(new ArtifactPathBuilder(), emitIntermediates: false);
        var runner = new PlanningSessionRunner(opts, model, mcp, artifactWriter);

        var res = await runner.RunAsync("RAW", CancellationToken.None);
        res.IsSuccess.Should().BeTrue();

        var order = mcp.ToolCallOrder;
        order.Should().Contain("get_server_info");
        order.Should().Contain("retrieve_memory_by_chunks");
        order.Should().Contain("merge_retrieval_results");
        order.Should().Contain("build_memory_context_pack");

        order.IndexOf("get_server_info").Should().BeLessThan(order.IndexOf("retrieve_memory_by_chunks"));
        order.IndexOf("retrieve_memory_by_chunks").Should().BeLessThan(order.IndexOf("merge_retrieval_results"));
        order.IndexOf("merge_retrieval_results").Should().BeLessThan(order.IndexOf("build_memory_context_pack"));
    }

    [Fact]
    public async Task worker_packet_is_not_built_when_execution_plan_validation_fails()
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
            retrievalFocuses = Array.Empty<string>(),
            ambiguities = Array.Empty<string>(),
            complexity = "low"
        });

        var mcp = new FakeMcpToolClient();

        var model = new InvalidPlanModelClient(requirementIntentJson);

        var opts = new AgentClientOptions(
            TaskFile: null,
            TaskText: "RAW",
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
            StdoutJson: false,
            PrintWorkerPacket: false);

        var artifactWriter = new PlanningArtifactWriter(new ArtifactPathBuilder(), emitIntermediates: false);
        var runner = new PlanningSessionRunner(opts, model, mcp, artifactWriter);

        var res = await runner.RunAsync("RAW", CancellationToken.None);
        res.IsSuccess.Should().BeFalse();

        File.Exists(Path.Combine(outDir, "11-worker-packet.md")).Should().BeFalse();
    }

    private sealed class OrderAssertingPlanningModelClient : IPlanningModelClient
    {
        private readonly string _requirementIntentJson;
        private readonly string _executionPlanHardConstraint;
        private readonly FakeMcpToolClient _mcpTool;
        private int _callIndex;

        public OrderAssertingPlanningModelClient(
            string requirementIntentJson,
            string executionPlanHardConstraint,
            FakeMcpToolClient mcpTool)
        {
            _requirementIntentJson = requirementIntentJson;
            _executionPlanHardConstraint = executionPlanHardConstraint;
            _mcpTool = mcpTool;
        }

        public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            _callIndex++;
            if (_callIndex == 1)
                return Task.FromResult(_requirementIntentJson);

            // Pass2 (execution plan synthesis) must happen only after context pack is built.
            _mcpTool.ToolCallOrder.Contains("build_memory_context_pack").Should().BeTrue();

            var sessionId = Extract(userPrompt, "sessionId");
            var taskId = Extract(userPrompt, "taskId");

            var plan = new
            {
                sessionId,
                taskId,
                objective = "objective",
                assumptions = Array.Empty<string>(),
                hardConstraints = new[] { _executionPlanHardConstraint },
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

            return Task.FromResult(JsonSerializer.Serialize(plan));
        }

        private static string Extract(string userPrompt, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                userPrompt,
                $"\"{key}\"\\s*:\\s*\"(?<v>[^\"]*)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) throw new InvalidOperationException("Missing key " + key);
            return m.Groups["v"].Value;
        }
    }

    private sealed class InvalidPlanModelClient : IPlanningModelClient
    {
        private readonly string _requirementIntentJson;
        private int _callIndex;

        public InvalidPlanModelClient(string requirementIntentJson)
        {
            _requirementIntentJson = requirementIntentJson;
        }

        public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            _callIndex++;
            if (_callIndex == 1)
                return Task.FromResult(_requirementIntentJson);

            var sessionId = System.Text.RegularExpressions.Regex.Match(
                userPrompt,
                "\"sessionId\"\\s*:\\s*\"(?<v>[^\"]*)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups["v"].Value;
            var taskId = System.Text.RegularExpressions.Regex.Match(
                userPrompt,
                "\"taskId\"\\s*:\\s*\"(?<v>[^\"]*)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups["v"].Value;

            // Missing acceptance checks -> validator should fail.
            var plan = new
            {
                sessionId,
                taskId,
                objective = "objective",
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
                        acceptanceChecks = Array.Empty<string>(),
                        supportingMemoryIds = Array.Empty<string>(),
                        notes = Array.Empty<string>()
                    }
                },
                validationChecks = Array.Empty<string>(),
                deliverables = new[] { "done" },
                openQuestions = Array.Empty<string>()
            };

            return Task.FromResult(JsonSerializer.Serialize(plan));
        }
    }
}

