using System.Text.Json;
using HarnessMcp.AgentClient.Artifacts;
using HarnessMcp.AgentClient.Config;
using HarnessMcp.AgentClient.Support;
using HarnessMcp.AgentClient.Transport;
using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Planning;

public sealed class PlanningSessionRunner
{
    private const string ProtocolName = "HarnessMcp.AgentClient.PlanTaskProtocol";
    private const string ProtocolVersion = "1.0";

    private readonly AgentClientOptions _options;
    private readonly IPlanningModelClient _planningModelClient;
    private readonly IMcpToolClient _mcpToolClient;
    private readonly PlanningArtifactWriter _artifactWriter;

    public PlanningSessionRunner(
        AgentClientOptions options,
        IPlanningModelClient planningModelClient,
        IMcpToolClient mcpToolClient,
        PlanningArtifactWriter artifactWriter)
    {
        _options = options;
        _planningModelClient = planningModelClient;
        _mcpToolClient = mcpToolClient;
        _artifactWriter = artifactWriter;
    }

    public async Task<RunResult<PlanningSessionRunnerResult>> RunAsync(string rawTask, CancellationToken cancellationToken)
    {
        var startedUtc = UtcClock.NowUtc();
        var endedUtc = startedUtc;

        var warnings = new List<string>();
        var errors = new List<string>();
        bool usedFallback = false;

        string sessionId = _options.SessionId ?? Ids.NewSessionId();
        string taskId = Ids.NewTaskId();

        // Build a deterministic artifact path map regardless of emit-intermediates.
        var paths = new ArtifactPathBuilder();
        var sessionJsonPath = paths.SessionJson(_options.OutputDir);
        var executionPlanMarkdownPath = paths.ExecutionPlanMd(_options.OutputDir);
        var workerPacketMarkdownPath = paths.WorkerPacketMd(_options.OutputDir);
        var manifestJsonPath = paths.HarnessRunManifestJson(_options.OutputDir);
        var artifactPaths = new Dictionary<string, string>
        {
            ["00-session.json"] = sessionJsonPath,
            ["10-execution-plan.md"] = executionPlanMarkdownPath,
            ["11-worker-packet.md"] = workerPacketMarkdownPath,
            ["12-harness-run-manifest.json"] = manifestJsonPath
        };

        async Task<string> WriteManifestAsync(bool success, string nextAction, bool usedFallbackSearches, string? workerPacketText, CancellationToken ct)
        {
            var manifest = new HarnessRunManifest(
                ProtocolName: ProtocolName,
                ProtocolVersion: ProtocolVersion,
                Success: success,
                SessionId: sessionId,
                TaskId: taskId,
                NextAction: nextAction,
                OutputDirectory: _options.OutputDir,
                ExecutionPlanMarkdownPath: executionPlanMarkdownPath,
                WorkerPacketMarkdownPath: workerPacketMarkdownPath,
                SessionJsonPath: sessionJsonPath,
                Warnings: warnings,
                Errors: errors,
                UsedFallbackSearches: usedFallbackSearches,
                WorkerPacketText: workerPacketText);

            var json = JsonSerializer.Serialize(manifest, JsonHelpers.Default);
            await File.WriteAllTextAsync(manifestJsonPath, json, ct).ConfigureAwait(false);
            return json;
        }

        try
        {
            // 3. preflight MCP with get_server_info
            await _mcpToolClient.GetServerInfoAsync(cancellationToken).ConfigureAwait(false);

            // 4. run requirement interpretation
            var interpretationService = new RequirementInterpretationService(_planningModelClient);
            var intent = await interpretationService.InterpretAsync(
                    sessionId, taskId, rawTask, _options.Project, _options.Domain, cancellationToken)
                .ConfigureAwait(false);

            // 5. validate requirement intent
            var intentValidation = new RequirementIntentValidator().Validate(intent);
            if (!intentValidation.IsSuccess)
            {
                errors.AddRange(intentValidation.Errors);
                warnings.AddRange(intentValidation.Warnings);
                endedUtc = UtcClock.NowUtc();
                await WriteSessionOnlyAsync(errors, warnings, usedFallback, sessionId, taskId, startedUtc, endedUtc, artifactPaths, null, cancellationToken)
                    .ConfigureAwait(false);
                await WriteManifestAsync(success: false, "fix_errors_and_rerun_harness", usedFallback, workerPacketText: null, cancellationToken)
                    .ConfigureAwait(false);
                return RunResult<PlanningSessionRunnerResult>.Failure(errors, warnings);
            }
            warnings.AddRange(intentValidation.Warnings);

            // 6. compile deterministic chunks
            var compiler = new RetrievalChunkCompiler(
                new ScopeInferenceService(),
                new ChunkTextNormalizer());
            var chunkSet = compiler.Compile(intent);

            // 7. run chunk quality gate
            var chunkGate = new ChunkQualityGate();
            var chunkQualityReport = chunkGate.Validate(chunkSet, intent);
            if (!chunkQualityReport.IsValid)
            {
                errors.AddRange(chunkQualityReport.Errors);
                Console.Error.WriteLine("Chunk quality gate failed:");
                foreach (var e in chunkQualityReport.Errors) Console.Error.WriteLine(" - " + e);

                if (_options.EmitIntermediates)
                {
                    var artifactPathsBuilderForSession = new ArtifactPathBuilder();
                    artifactPaths["03-retrieval-chunks.json"] = artifactPathsBuilderForSession.RetrievalChunksJson(_options.OutputDir);
                    artifactPaths["04-chunk-quality-report.json"] = artifactPathsBuilderForSession.ChunkQualityReportJson(_options.OutputDir);
                }

                // Always include the detailed report in 00-session.json.
                await WriteSessionOnlyAsync(
                        errors,
                        warnings,
                        usedFallback,
                        sessionId,
                        taskId,
                        startedUtc,
                        endedUtc,
                        artifactPaths,
                        chunkQualityReport,
                        cancellationToken)
                    .ConfigureAwait(false);

                // Optionally also emit 03/04 artifacts for traceability.
                if (_options.EmitIntermediates)
                {
                    var jsonOpts = JsonHelpers.Default;
                    var artifactPathsBuilder = new ArtifactPathBuilder();
                    await File.WriteAllTextAsync(
                            artifactPathsBuilder.RetrievalChunksJson(_options.OutputDir),
                            JsonSerializer.Serialize(chunkSet, jsonOpts),
                            cancellationToken)
                        .ConfigureAwait(false);
                    await File.WriteAllTextAsync(
                            artifactPathsBuilder.ChunkQualityReportJson(_options.OutputDir),
                            JsonSerializer.Serialize(chunkQualityReport, jsonOpts),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                await WriteManifestAsync(success: false, "fix_errors_and_rerun_harness", usedFallback, workerPacketText: null, cancellationToken)
                    .ConfigureAwait(false);
                return RunResult<PlanningSessionRunnerResult>.Failure(errors);
            }

            // 8-11. run primary memory retrieval pipeline
            var mapper = new McpRequestMapper();
            var orchestrator = new MemoryRetrievalOrchestrator(_mcpToolClient, mapper);
            var primary = await orchestrator.RetrievePrimaryAsync(intent, chunkSet, _options, cancellationToken).ConfigureAwait(false);

            // 12. run targeted fallback retrieval if rules require it
            var fallbackPlanner = new FallbackRetrievalPlanner(_mcpToolClient, mapper, new ScopeInferenceService());
            var fallbackResult = await fallbackPlanner.PlanAndRunFallbacksAsync(intent, chunkSet, primary.Merged, _options.MinimumAuthority, cancellationToken).ConfigureAwait(false);
            usedFallback = fallbackResult.UsedFallbackSearches;
            warnings.AddRange(fallbackResult.Diagnostics);

            // 13. hydrate final selected items with get_knowledge_item
            var evidenceHydrator = new MemoryEvidenceHydrator(_mcpToolClient, mapper);
            var hydrated = await evidenceHydrator.HydrateFinalSelectedItemsAsync(primary.ContextPack, taskId, fallbackResult.HydratedFallbackItems, cancellationToken).ConfigureAwait(false);

            // 14. build planning-memory bundle
            var bundle = new PlanningMemoryBundle(
                primary.Retrieved,
                primary.Merged,
                primary.ContextPack,
                hydrated,
                fallbackResult.FallbackSearches,
                primary.Diagnostics,
                usedFallback);

            // 15. summarize planning memory
            var summarizer = new PlanningContextSummarizer();
            var summaryMarkdown = summarizer.Summarize(bundle);

            // 16. run execution-plan synthesis
            var planService = new ExecutionPlanService(_planningModelClient, new ExecutionPlanPromptBuilder());
            var plan = await planService.SynthesizeAsync(rawTask, intent, chunkSet, summaryMarkdown, cancellationToken).ConfigureAwait(false);

            // 17. validate execution plan
            var planValidation = new ExecutionPlanValidator().Validate(plan, intent, primary.ContextPack);
            if (!planValidation.IsSuccess)
            {
                errors.AddRange(planValidation.Errors);
                warnings.AddRange(planValidation.Warnings);
                await WriteSessionOnlyAsync(errors, warnings, usedFallback, sessionId, taskId, startedUtc, endedUtc, artifactPaths, null, cancellationToken)
                    .ConfigureAwait(false);
                await WriteManifestAsync(success: false, "fix_errors_and_rerun_harness", usedFallback, workerPacketText: null, cancellationToken)
                    .ConfigureAwait(false);
                return RunResult<PlanningSessionRunnerResult>.Failure(errors, warnings);
            }

            warnings.AddRange(planValidation.Warnings);

            // 18. build worker packet
            var packetBuilder = new WorkerPacketBuilder();
            var workerPacket = packetBuilder.Build(intent, plan, hydrated);

            // 19. write artifacts
            var execMdPath = paths.ExecutionPlanMd(_options.OutputDir);
            var workerMdPath = paths.WorkerPacketMd(_options.OutputDir);
            artifactPaths["10-execution-plan.md"] = execMdPath;
            artifactPaths["11-worker-packet.md"] = workerMdPath;

            // Emit intermediate artifact paths when requested
            if (_options.EmitIntermediates)
            {
                artifactPaths["01-raw-task.txt"] = paths.RawTaskText(_options.OutputDir);
                artifactPaths["02-requirement-intent.json"] = paths.RequirementIntentJson(_options.OutputDir);
                artifactPaths["03-retrieval-chunks.json"] = paths.RetrievalChunksJson(_options.OutputDir);
                artifactPaths["04-chunk-quality-report.json"] = paths.ChunkQualityReportJson(_options.OutputDir);
                artifactPaths["05-retrieve-memory-by-chunks.json"] = paths.RetrievedMemoryByChunksJson(_options.OutputDir);
                artifactPaths["06-merge-retrieval-results.json"] = paths.MergedRetrievalResultsJson(_options.OutputDir);
                artifactPaths["07-build-memory-context-pack.json"] = paths.BuildMemoryContextPackJson(_options.OutputDir);
                artifactPaths["08-planning-memory-summary.md"] = paths.PlanningMemorySummaryMd(_options.OutputDir);
                artifactPaths["09-execution-plan.json"] = paths.ExecutionPlanJson(_options.OutputDir);
            }

            endedUtc = UtcClock.NowUtc();

            var sessionJson = new
            {
                sessionId,
                taskId,
                startedUtc = startedUtc.ToString("O"),
                endedUtc = endedUtc.ToString("O"),
                mcpBaseUrl = _options.McpBaseUrl,
                modelBaseUrl = _options.ModelBaseUrl,
                modelName = _options.ModelName,
                usedFallbackSearches = usedFallback,
                warnings,
                errors,
                artifactPaths
            };

            await _artifactWriter.WriteSessionAndArtifactsAsync(
                    _options.OutputDir,
                    sessionJson: sessionJson,
                    rawTask: rawTask,
                    requirementIntent: intent,
                    retrievalChunkSet: chunkSet,
                    chunkQualityReport: chunkQualityReport,
                    retrieved: primary.Retrieved,
                    merged: primary.Merged,
                    contextPack: primary.ContextPack,
                    planningMemorySummaryMarkdown: summaryMarkdown,
                    executionPlan: plan,
                    workerPacket: workerPacket)
                .ConfigureAwait(false);

            var workerPacketText = _options.PrintWorkerPacket
                ? MarkdownRenderers.RenderWorkerPacketMarkdown(workerPacket)
                : null;
            await WriteManifestAsync(
                    success: true,
                    nextAction: "paste_worker_packet_into_execution_agent",
                    usedFallbackSearches: usedFallback,
                    workerPacketText: workerPacketText,
                    ct: cancellationToken)
                .ConfigureAwait(false);

            return RunResult.Success(new PlanningSessionRunnerResult(execMdPath, workerMdPath), warnings);
        }
        catch (Exception ex)
        {
            endedUtc = UtcClock.NowUtc();
            errors.Add(ex.ToString());
            await WriteSessionOnlyAsync(errors, warnings, usedFallback, sessionId, taskId, startedUtc, endedUtc, artifactPaths, null, cancellationToken)
                .ConfigureAwait(false);
            await WriteManifestAsync(success: false, "fix_errors_and_rerun_harness", usedFallback, workerPacketText: null, cancellationToken)
                .ConfigureAwait(false);
            return RunResult<PlanningSessionRunnerResult>.Failure(errors, warnings);
        }
    }

    private async Task WriteSessionOnlyAsync(
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        bool usedFallback,
        string sessionId,
        string taskId,
        DateTimeOffset startedUtc,
        DateTimeOffset endedUtc,
        IReadOnlyDictionary<string, string> artifactPaths,
        ChunkQualityReport? chunkQualityReport,
        CancellationToken cancellationToken)
    {
        var paths = new ArtifactPathBuilder();
        var sessionPath = paths.SessionJson(_options.OutputDir);
        var sessionJson = new
        {
            sessionId,
            taskId,
            startedUtc = startedUtc.ToString("O"),
            endedUtc = endedUtc.ToString("O"),
            mcpBaseUrl = _options.McpBaseUrl,
            modelBaseUrl = _options.ModelBaseUrl,
            modelName = _options.ModelName,
            usedFallbackSearches = usedFallback,
            warnings,
            errors,
            chunkQualityReport,
            artifactPaths
        };

        var jsonOpts = JsonHelpers.Default;
        await File.WriteAllTextAsync(sessionPath, JsonSerializer.Serialize(sessionJson, jsonOpts), cancellationToken).ConfigureAwait(false);
    }

}

public sealed record PlanningSessionRunnerResult(string ExecutionPlanMarkdownPath, string WorkerPacketMarkdownPath);

