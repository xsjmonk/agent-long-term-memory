using System.Text;
using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Artifacts;

public sealed class PlanningArtifactWriter
{
    private readonly ArtifactPathBuilder _paths;
    private readonly bool _emitIntermediates;

    public PlanningArtifactWriter(
        ArtifactPathBuilder paths,
        bool emitIntermediates)
    {
        _paths = paths;
        _emitIntermediates = emitIntermediates;
    }

    public async Task WriteSessionAndArtifactsAsync(
        string outputDir,
        object sessionJson,
        string rawTask,
        RequirementIntent requirementIntent,
        RetrievalChunkSet retrievalChunkSet,
        ChunkQualityReport chunkQualityReport,
        RetrieveMemoryByChunksResponse retrieved,
        MergeRetrievalResultsResponse merged,
        BuildMemoryContextPackResponse contextPack,
        string planningMemorySummaryMarkdown,
        ExecutionPlan executionPlan,
        WorkerExecutionPacket workerPacket)
    {
        var sessionPath = _paths.SessionJson(outputDir);

        var jsonOpts = JsonHelpers.Default;

        // Always emit 00-session.json
        await File.WriteAllTextAsync(sessionPath, JsonSerializer.Serialize(sessionJson, jsonOpts), Encoding.UTF8)
            .ConfigureAwait(false);

        if (_emitIntermediates)
        {
            await File.WriteAllTextAsync(_paths.RawTaskText(outputDir), rawTask, Encoding.UTF8).ConfigureAwait(false);
            await File.WriteAllTextAsync(_paths.RequirementIntentJson(outputDir), JsonSerializer.Serialize(requirementIntent, jsonOpts), Encoding.UTF8).ConfigureAwait(false);
            await File.WriteAllTextAsync(_paths.RetrievalChunksJson(outputDir), JsonSerializer.Serialize(retrievalChunkSet, jsonOpts), Encoding.UTF8).ConfigureAwait(false);
            await File.WriteAllTextAsync(_paths.ChunkQualityReportJson(outputDir), JsonSerializer.Serialize(chunkQualityReport, jsonOpts), Encoding.UTF8).ConfigureAwait(false);
            await File.WriteAllTextAsync(_paths.RetrievedMemoryByChunksJson(outputDir), JsonSerializer.Serialize(retrieved, jsonOpts), Encoding.UTF8).ConfigureAwait(false);
            await File.WriteAllTextAsync(_paths.MergedRetrievalResultsJson(outputDir), JsonSerializer.Serialize(merged, jsonOpts), Encoding.UTF8).ConfigureAwait(false);
            await File.WriteAllTextAsync(_paths.BuildMemoryContextPackJson(outputDir), JsonSerializer.Serialize(contextPack, jsonOpts), Encoding.UTF8).ConfigureAwait(false);
            await File.WriteAllTextAsync(_paths.PlanningMemorySummaryMd(outputDir), planningMemorySummaryMarkdown, Encoding.UTF8).ConfigureAwait(false);
            await File.WriteAllTextAsync(_paths.ExecutionPlanJson(outputDir), JsonSerializer.Serialize(executionPlan, jsonOpts), Encoding.UTF8).ConfigureAwait(false);
        }

        // Always emit the final two markdown artifacts. (JSON can be omitted when intermediates are disabled.)
        await File.WriteAllTextAsync(_paths.ExecutionPlanMd(outputDir), MarkdownRenderers.RenderExecutionPlanMarkdown(executionPlan), Encoding.UTF8)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(_paths.WorkerPacketMd(outputDir), MarkdownRenderers.RenderWorkerPacketMarkdown(workerPacket), Encoding.UTF8)
            .ConfigureAwait(false);

        // When intermediates are enabled, also emit 10/11 JSON if present (but 10/11 already).
        if (_emitIntermediates)
        {
            // Worker packet doesn't have JSON artifact per required list.
        }
    }
}

