using System.Text.Json;
using FluentAssertions;
using HarnessMcp.AgentClient.Artifacts;
using HarnessMcp.AgentClient.Config;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.AgentClient.Transport;
using HarnessMcp.Contracts;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class PlanningSessionRunnerTests
{
    private static AgentClientOptions CreateOpts(string outputDir, bool emitIntermediates)
        => new AgentClientOptions(
            TaskFile: null,
            TaskText: "ignored",
            OutputDir: outputDir,
            McpBaseUrl: "http://example/mcp",
            ModelBaseUrl: "http://example",
            ModelName: "fake-model",
            ApiKeyEnv: "OPENAI_API_KEY",
            SessionId: null,
            Project: null,
            Domain: null,
            MaxItemsPerChunk: 5,
            MinimumAuthority: AuthorityLevel.Reviewed,
            EmitIntermediates: emitIntermediates,
            StdoutJson: false,
            PrintWorkerPacket: false);

    private static MergedKnowledgeItemDto MergedItem(Guid id, RetrievalClass cls, string title)
    {
        var candidate = new KnowledgeCandidateDto(
            KnowledgeItemId: id,
            RetrievalClass: cls,
            Title: title,
            Summary: "s",
            Details: null,
            SemanticScore: 0,
            LexicalScore: 0,
            ScopeScore: 0,
            AuthorityScore: 0,
            CaseShapeScore: 0,
            FinalScore: 1,
            Authority: AuthorityLevel.Reviewed,
            Status: KnowledgeStatus.Active,
            Scopes: new ScopeFilterDto(
                Domains: Array.Empty<string>(),
                Modules: Array.Empty<string>(),
                Features: Array.Empty<string>(),
                Layers: Array.Empty<string>(),
                Concerns: Array.Empty<string>(),
                Repos: Array.Empty<string>(),
                Services: Array.Empty<string>(),
                Symbols: Array.Empty<string>()),
            Labels: Array.Empty<string>(),
            Tags: Array.Empty<string>(),
            Evidence: Array.Empty<EvidenceDto>(),
            SupportedByChunks: Array.Empty<string>(),
            SupportedByQueryKinds: Array.Empty<string>());

        return new MergedKnowledgeItemDto(
            Item: candidate,
            SupportedByChunkIds: Array.Empty<string>(),
            SupportedByChunkTypes: Array.Empty<ChunkType>(),
            MergeRationales: Array.Empty<string>());
    }

    private static string RequirementIntentJson(
        string complexity,
        string goal = "do it",
        string[]? hardConstraints = null,
        string[]? requestedOperations = null,
        string[]? riskSignals = null)
    {
        var hc = hardConstraints ?? new[] { "engine logic must not change" };
        var ro = requestedOperations ?? new[] { "ajax refresh" };
        var rs = riskSignals ?? Array.Empty<string>();

        var obj = new
        {
            taskType = "core-task",
            domain = (string?)null,
            module = (string?)null,
            feature = (string?)null,
            goal = goal,
            requestedOperations = ro,
            hardConstraints = hc,
            softConstraints = Array.Empty<string>(),
            riskSignals = rs,
            candidateLayers = Array.Empty<string>(),
            retrievalFocuses = Array.Empty<string>(),
            ambiguities = Array.Empty<string>(),
            complexity = complexity
        };
        return JsonSerializer.Serialize(obj);
    }

    private static string ExecutionPlanJson(
        string sessionId,
        string taskId,
        string objective,
        string[] hardConstraints)
    {
        var obj = new
        {
            sessionId = sessionId,
            taskId = taskId,
            objective = objective,
            assumptions = Array.Empty<string>(),
            hardConstraints = hardConstraints,
            antiPatternsToAvoid = Array.Empty<string>(),
            steps = new[]
            {
                new
                {
                    stepNumber = 1,
                    title = "Step 1",
                    purpose = "Do work",
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
        return JsonSerializer.Serialize(obj);
    }

    [Fact]
    public async Task happy_path_emits_all_intermediate_artifacts()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "harness-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        var fakeMcp = new FakeMcpToolClient();

        var decisionId = Guid.NewGuid();
        var constraintId = Guid.NewGuid();
        var bestId = Guid.NewGuid();
        var similarId = Guid.NewGuid();

        fakeMcp.SeedKnowledgeItem(decisionId, RetrievalClass.Decision, "Decision A");
        fakeMcp.SeedKnowledgeItem(constraintId, RetrievalClass.Constraint, "Constraint A");
        fakeMcp.SeedKnowledgeItem(bestId, RetrievalClass.BestPractice, "Best A");
        fakeMcp.SeedKnowledgeItem(similarId, RetrievalClass.SimilarCase, "Similar A");

        fakeMcp.Merged = new MergeRetrievalResultsResponse(
            SchemaVersion: "1.0",
            Kind: "merge_retrieval_results",
            RequestId: "r",
            TaskId: "t",
            Decisions: new[] { MergedItem(decisionId, RetrievalClass.Decision, "Decision A") },
            Constraints: new[] { MergedItem(constraintId, RetrievalClass.Constraint, "Constraint A") },
            BestPractices: new[] { MergedItem(bestId, RetrievalClass.BestPractice, "Best A") },
            AntiPatterns: Array.Empty<MergedKnowledgeItemDto>(),
            SimilarCases: new[] { MergedItem(similarId, RetrievalClass.SimilarCase, "Similar A") },
            References: Array.Empty<MergedKnowledgeItemDto>(),
            Structures: Array.Empty<MergedKnowledgeItemDto>(),
            Warnings: Array.Empty<string>(),
            ElapsedMs: 1);

        fakeMcp.ContextPack = new BuildMemoryContextPackResponse(
            SchemaVersion: "1.0",
            Kind: "build_memory_context_pack",
            RequestId: "r",
            TaskId: "t",
            ContextPack: new ContextPackSectionDto(
                Decisions: new[] { MergedItem(decisionId, RetrievalClass.Decision, "Decision A") },
                Constraints: new[] { MergedItem(constraintId, RetrievalClass.Constraint, "Constraint A") },
                BestPractices: new[] { MergedItem(bestId, RetrievalClass.BestPractice, "Best A") },
                AntiPatterns: Array.Empty<MergedKnowledgeItemDto>(),
                SimilarCases: new[] { MergedItem(similarId, RetrievalClass.SimilarCase, "Similar A") },
                References: Array.Empty<MergedKnowledgeItemDto>(),
                Structures: Array.Empty<MergedKnowledgeItemDto>()),
            Diagnostics: new ContextPackDiagnosticsDto(
                ChunksProcessed: 0,
                DistinctKnowledgeItems: 0,
                RetrievalElapsedMs: 0,
                MergeElapsedMs: 0,
                AssemblyElapsedMs: 0,
                Warnings: Array.Empty<string>()));

        // Model pass1 output (intent) and pass2 output (plan) are queued.
        var intentJson = RequirementIntentJson("low");
        // Runner generates sessionId/taskId at runtime, so we need a planning model that echoes them in pass2.
        // We'll handle that by computing pass2 output inside a custom fake.
        var fakeModel = new EchoingExecutionPlanModelClient(intentJson);

        var opts = CreateOpts(outDir, emitIntermediates: true);
        var artifactWriter = new PlanningArtifactWriter(new ArtifactPathBuilder(), emitIntermediates: true);
        var runner = new PlanningSessionRunner(opts, fakeModel, fakeMcp, artifactWriter);

        var res = await runner.RunAsync("RAW", CancellationToken.None);
        res.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(outDir, "00-session.json")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "01-raw-task.txt")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "10-execution-plan.md")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "11-worker-packet.md")).Should().BeTrue();
    }

    [Fact]
    public async Task fallback_similar_case_path_calls_search_and_hydrates()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "harness-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        var fakeMcp = new FakeMcpToolClient();

        // Primary retrieval: merged similar cases empty; context pack similar cases empty.
        fakeMcp.Merged = new MergeRetrievalResultsResponse(
            SchemaVersion: "1.0",
            Kind: "merge_retrieval_results",
            RequestId: "r",
            TaskId: "t",
            Decisions: Array.Empty<MergedKnowledgeItemDto>(),
            Constraints: Array.Empty<MergedKnowledgeItemDto>(),
            BestPractices: Array.Empty<MergedKnowledgeItemDto>(),
            AntiPatterns: Array.Empty<MergedKnowledgeItemDto>(),
            SimilarCases: Array.Empty<MergedKnowledgeItemDto>(),
            References: Array.Empty<MergedKnowledgeItemDto>(),
            Structures: Array.Empty<MergedKnowledgeItemDto>(),
            Warnings: Array.Empty<string>(),
            ElapsedMs: 1);

        fakeMcp.ContextPack = new BuildMemoryContextPackResponse(
            SchemaVersion: "1.0",
            Kind: "build_memory_context_pack",
            RequestId: "r",
            TaskId: "t",
            ContextPack: new ContextPackSectionDto(
                Decisions: Array.Empty<MergedKnowledgeItemDto>(),
                Constraints: Array.Empty<MergedKnowledgeItemDto>(),
                BestPractices: Array.Empty<MergedKnowledgeItemDto>(),
                AntiPatterns: Array.Empty<MergedKnowledgeItemDto>(),
                SimilarCases: Array.Empty<MergedKnowledgeItemDto>(),
                References: Array.Empty<MergedKnowledgeItemDto>(),
                Structures: Array.Empty<MergedKnowledgeItemDto>()),
            Diagnostics: new ContextPackDiagnosticsDto(
                ChunksProcessed: 0,
                DistinctKnowledgeItems: 0,
                RetrievalElapsedMs: 0,
                MergeElapsedMs: 0,
                AssemblyElapsedMs: 0,
                Warnings: Array.Empty<string>()));

        var intentJson = RequirementIntentJson("medium", hardConstraints: new[] { "hc" }, requestedOperations: new[] { "op" });
        var fakeModel = new EchoingExecutionPlanModelClient(intentJson);

        var opts = CreateOpts(outDir, emitIntermediates: false);
        var artifactWriter = new PlanningArtifactWriter(new ArtifactPathBuilder(), emitIntermediates: false);
        var runner = new PlanningSessionRunner(opts, fakeModel, fakeMcp, artifactWriter);

        var res = await runner.RunAsync("RAW", CancellationToken.None);
        res.IsSuccess.Should().BeTrue();

        fakeMcp.SearchCalls.Should().Contain(c => c.QueryKind == QueryKind.SimilarCase);
        fakeMcp.KnowledgeItemCalls.Count.Should().BeGreaterThanOrEqualTo(3);
        File.Exists(Path.Combine(outDir, "10-execution-plan.md")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "11-worker-packet.md")).Should().BeTrue();
    }

    [Fact]
    public async Task fallback_constraints_path_calls_search_for_each_hard_constraint()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "harness-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        var fakeMcp = new FakeMcpToolClient();

        // Primary retrieval: merged constraints empty triggers fallback.
        fakeMcp.Merged = new MergeRetrievalResultsResponse(
            SchemaVersion: "1.0",
            Kind: "merge_retrieval_results",
            RequestId: "r",
            TaskId: "t",
            Decisions: Array.Empty<MergedKnowledgeItemDto>(),
            Constraints: Array.Empty<MergedKnowledgeItemDto>(),
            BestPractices: Array.Empty<MergedKnowledgeItemDto>(),
            AntiPatterns: Array.Empty<MergedKnowledgeItemDto>(),
            SimilarCases: new[] { MergedItem(Guid.NewGuid(), RetrievalClass.SimilarCase, "x") }, // avoid Rule A for medium
            References: Array.Empty<MergedKnowledgeItemDto>(),
            Structures: Array.Empty<MergedKnowledgeItemDto>(),
            Warnings: Array.Empty<string>(),
            ElapsedMs: 1);

        fakeMcp.ContextPack = new BuildMemoryContextPackResponse(
            SchemaVersion: "1.0",
            Kind: "build_memory_context_pack",
            RequestId: "r",
            TaskId: "t",
            ContextPack: new ContextPackSectionDto(
                Decisions: Array.Empty<MergedKnowledgeItemDto>(),
                Constraints: Array.Empty<MergedKnowledgeItemDto>(),
                BestPractices: Array.Empty<MergedKnowledgeItemDto>(),
                AntiPatterns: Array.Empty<MergedKnowledgeItemDto>(),
                SimilarCases: Array.Empty<MergedKnowledgeItemDto>(),
                References: Array.Empty<MergedKnowledgeItemDto>(),
                Structures: Array.Empty<MergedKnowledgeItemDto>()),
            Diagnostics: new ContextPackDiagnosticsDto(
                ChunksProcessed: 0,
                DistinctKnowledgeItems: 0,
                RetrievalElapsedMs: 0,
                MergeElapsedMs: 0,
                AssemblyElapsedMs: 0,
                Warnings: Array.Empty<string>()));

        var hc = new[] { "hc1", "hc2" };
        var intentJson = RequirementIntentJson("low", hardConstraints: hc, requestedOperations: new[] { "op" });
        var fakeModel = new EchoingExecutionPlanModelClient(intentJson);

        var opts = CreateOpts(outDir, emitIntermediates: false);
        var artifactWriter = new PlanningArtifactWriter(new ArtifactPathBuilder(), emitIntermediates: false);
        var runner = new PlanningSessionRunner(opts, fakeModel, fakeMcp, artifactWriter);

        var res = await runner.RunAsync("RAW", CancellationToken.None);
        res.IsSuccess.Should().BeTrue();

        // Two searches, one per hard constraint.
        fakeMcp.SearchCalls.Count(c => c.QueryKind == QueryKind.Constraint).Should().Be(hc.Length);
        File.Exists(Path.Combine(outDir, "00-session.json")).Should().BeTrue();
    }

    [Fact]
    public async Task artifact_emission_path_when_emit_intermediates_false()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "harness-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        var fakeMcp = new FakeMcpToolClient();

        // Minimal primary retrieval: constraints non-empty to avoid fallback.
        var constraintId = Guid.NewGuid();
        fakeMcp.SeedKnowledgeItem(constraintId, RetrievalClass.Constraint, "Constraint A");

        fakeMcp.Merged = new MergeRetrievalResultsResponse(
            SchemaVersion: "1.0",
            Kind: "merge_retrieval_results",
            RequestId: "r",
            TaskId: "t",
            Decisions: Array.Empty<MergedKnowledgeItemDto>(),
            Constraints: new[] { MergedItem(constraintId, RetrievalClass.Constraint, "Constraint A") },
            BestPractices: Array.Empty<MergedKnowledgeItemDto>(),
            AntiPatterns: Array.Empty<MergedKnowledgeItemDto>(),
            SimilarCases: Array.Empty<MergedKnowledgeItemDto>(),
            References: Array.Empty<MergedKnowledgeItemDto>(),
            Structures: Array.Empty<MergedKnowledgeItemDto>(),
            Warnings: Array.Empty<string>(),
            ElapsedMs: 1);

        fakeMcp.ContextPack = new BuildMemoryContextPackResponse(
            SchemaVersion: "1.0",
            Kind: "build_memory_context_pack",
            RequestId: "r",
            TaskId: "t",
            ContextPack: new ContextPackSectionDto(
                Decisions: Array.Empty<MergedKnowledgeItemDto>(),
                Constraints: new[] { MergedItem(constraintId, RetrievalClass.Constraint, "Constraint A") },
                BestPractices: Array.Empty<MergedKnowledgeItemDto>(),
                AntiPatterns: Array.Empty<MergedKnowledgeItemDto>(),
                SimilarCases: Array.Empty<MergedKnowledgeItemDto>(),
                References: Array.Empty<MergedKnowledgeItemDto>(),
                Structures: Array.Empty<MergedKnowledgeItemDto>()),
            Diagnostics: new ContextPackDiagnosticsDto(
                ChunksProcessed: 0,
                DistinctKnowledgeItems: 0,
                RetrievalElapsedMs: 0,
                MergeElapsedMs: 0,
                AssemblyElapsedMs: 0,
                Warnings: Array.Empty<string>()));

        var intentJson = RequirementIntentJson("low", hardConstraints: new[] { "hc" }, requestedOperations: new[] { "op" });
        var fakeModel = new EchoingExecutionPlanModelClient(intentJson);

        var opts = CreateOpts(outDir, emitIntermediates: false);
        var artifactWriter = new PlanningArtifactWriter(new ArtifactPathBuilder(), emitIntermediates: false);
        var runner = new PlanningSessionRunner(opts, fakeModel, fakeMcp, artifactWriter);

        var res = await runner.RunAsync("RAW", CancellationToken.None);
        res.IsSuccess.Should().BeTrue();

        File.Exists(Path.Combine(outDir, "00-session.json")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "10-execution-plan.md")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "11-worker-packet.md")).Should().BeTrue();

        File.Exists(Path.Combine(outDir, "01-raw-task.txt")).Should().BeFalse();
        File.Exists(Path.Combine(outDir, "09-execution-plan.json")).Should().BeFalse();
    }

    private sealed class EchoingExecutionPlanModelClient : IPlanningModelClient
    {
        private readonly string _intentJson;
        private int _callIndex;

        public EchoingExecutionPlanModelClient(string intentJson)
        {
            _intentJson = intentJson;
        }

        public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            _callIndex++;
            if (_callIndex == 1)
                return Task.FromResult(_intentJson);

            // Extract task/session ids from the user prompt JSON snippets.
            // The runner calls the plan prompt builder, which JSON-serializes requirement intent and chunk set.
            // We'll parse the user prompt and look for "sessionId"/"taskId".
            var idx = userPrompt.IndexOf("\"sessionId\"", StringComparison.OrdinalIgnoreCase);
            var idx2 = userPrompt.IndexOf("\"taskId\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0 || idx2 < 0)
                throw new InvalidOperationException("Could not find ids in user prompt.");

            // Simpler: the runner already supplies sessionId and taskId to model pass2 via prompt builder serialization.
            // We'll use a regex to capture them.
            var sessionId = ExtractString(userPrompt, "sessionId");
            var taskId = ExtractString(userPrompt, "taskId");

            var intent = JsonSerializer.Deserialize<JsonElement>(_intentJson);
            var hardConstraints = intent.GetProperty("hardConstraints").EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

            var planJson = ExecutionPlanJson(sessionId, taskId, objective: "objective", hardConstraints: hardConstraints);
            return Task.FromResult(planJson);
        }

        private static string ExtractString(string text, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, $"\"{key}\"\\s*:\\s*\"(?<v>[^\"]*)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) throw new InvalidOperationException($"Missing {key} in prompt.");
            return m.Groups["v"].Value;
        }
    }
}

