using System.Diagnostics;
using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using ModelContextProtocol.Server;

namespace HarnessMcp.Transport.Mcp;

[McpServerToolType]
public sealed class KnowledgeQueryTools(
    IChunkRetrievalService chunkRetrievalService,
    IRetrievalMergeService mergeService,
    IMemoryContextPackService contextPackService,
    IKnowledgeSearchService searchService,
    IKnowledgeReadService readService,
    IRelatedKnowledgeService relatedService,
    IAppInfoProvider appInfoProvider,
    IMonitorEventSink monitorEventSink,
    int maxPayloadPreviewChars)
{
    private string Trim(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return string.Empty;
        var max = Math.Max(64, maxPayloadPreviewChars);
        return json.Length <= max ? json : json[..max] + "…";
    }

    private void Start(string tool, string? requestId, string? taskId) =>
        monitorEventSink.Publish(new MonitorEventDto(
            0, DateTimeOffset.UtcNow, MonitorEventKind.RequestStart, requestId, tool, taskId,
            "Information", $"{tool} start", null));

    private void Fail(string tool, string? requestId, string? taskId, Exception ex) =>
        monitorEventSink.Publish(new MonitorEventDto(
            0, DateTimeOffset.UtcNow, MonitorEventKind.RequestFailure, requestId, tool, taskId,
            "Error", ex.Message, Trim(ex.ToString())));

    [McpServerTool(Name = "retrieve_memory_by_chunks")]
    public async ValueTask<RetrieveMemoryByChunksResponse> RetrieveMemoryByChunks(
        RetrieveMemoryByChunksRequest request,
        CancellationToken cancellationToken)
    {
        Start("retrieve_memory_by_chunks", request.RequestId, request.TaskId);
        var sw = Stopwatch.StartNew();
        try
        {
            var resp = await chunkRetrievalService.RetrieveMemoryByChunksAsync(request, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            var total = resp.ChunkResults.Sum(c =>
                c.Results.Decisions.Count + c.Results.BestPractices.Count + c.Results.Antipatterns.Count +
                c.Results.SimilarCases.Count + c.Results.Constraints.Count + c.Results.References.Count +
                c.Results.Structures.Count);
            var summary = JsonSerializer.Serialize(new
            {
                chunkCount = resp.ChunkResults.Count,
                totalItems = total,
                notes = resp.Notes.Count,
                elapsedMs = resp.ElapsedMs
            });
            monitorEventSink.Publish(new MonitorEventDto(
                0, DateTimeOffset.UtcNow, MonitorEventKind.RequestSuccess, request.RequestId,
                "retrieve_memory_by_chunks", request.TaskId, "Information",
                $"chunks={resp.ChunkResults.Count} items={total} ms={resp.ElapsedMs}", Trim(summary)));
            return resp;
        }
        catch (Exception ex)
        {
            Fail("retrieve_memory_by_chunks", request.RequestId, request.TaskId, ex);
            throw;
        }
    }

    [McpServerTool(Name = "merge_retrieval_results")]
    public async ValueTask<MergeRetrievalResultsResponse> MergeRetrievalResults(
        MergeRetrievalResultsRequest request,
        CancellationToken cancellationToken)
    {
        Start("merge_retrieval_results", request.RequestId, request.TaskId);
        try
        {
            var resp = await mergeService.MergeRetrievalResultsAsync(request, cancellationToken).ConfigureAwait(false);
            var summary = JsonSerializer.Serialize(new
            {
                decisions = resp.Decisions.Count,
                constraints = resp.Constraints.Count,
                best = resp.BestPractices.Count,
                anti = resp.AntiPatterns.Count,
                similar = resp.SimilarCases.Count,
                refs = resp.References.Count,
                structures = resp.Structures.Count,
                warnings = resp.Warnings.Count,
                elapsedMs = resp.ElapsedMs
            });
            monitorEventSink.Publish(new MonitorEventDto(
                0, DateTimeOffset.UtcNow, MonitorEventKind.MergeTiming, request.RequestId,
                "merge_retrieval_results", request.TaskId, "Information",
                $"merged sections ms={resp.ElapsedMs}", Trim(summary)));
            monitorEventSink.Publish(new MonitorEventDto(
                0, DateTimeOffset.UtcNow, MonitorEventKind.RequestSuccess, request.RequestId,
                "merge_retrieval_results", request.TaskId, "Information",
                $"warnings={resp.Warnings.Count}", Trim(summary)));
            return resp;
        }
        catch (Exception ex)
        {
            Fail("merge_retrieval_results", request.RequestId, request.TaskId, ex);
            throw;
        }
    }

    [McpServerTool(Name = "build_memory_context_pack")]
    public async ValueTask<BuildMemoryContextPackResponse> BuildMemoryContextPack(
        BuildMemoryContextPackRequest request,
        CancellationToken cancellationToken)
    {
        Start("build_memory_context_pack", request.RequestId, request.TaskId);
        try
        {
            var resp = await contextPackService.BuildMemoryContextPackAsync(request, cancellationToken).ConfigureAwait(false);
            var s = resp.ContextPack;
            var summary = JsonSerializer.Serialize(new
            {
                taskId = request.TaskId,
                decisions = s.Decisions.Count,
                constraints = s.Constraints.Count,
                best = s.BestPractices.Count,
                anti = s.AntiPatterns.Count,
                similar = s.SimilarCases.Count,
                refs = s.References.Count,
                structures = s.Structures.Count,
                warnings = resp.Diagnostics.Warnings.Count,
                elapsedMs = resp.Diagnostics.AssemblyElapsedMs
            });
            monitorEventSink.Publish(new MonitorEventDto(
                0, DateTimeOffset.UtcNow, MonitorEventKind.ContextPackBuilt, request.RequestId,
                "build_memory_context_pack", request.TaskId, "Information",
                $"pack built warnings={resp.Diagnostics.Warnings.Count}", Trim(summary)));
            monitorEventSink.Publish(new MonitorEventDto(
                0, DateTimeOffset.UtcNow, MonitorEventKind.RequestSuccess, request.RequestId,
                "build_memory_context_pack", request.TaskId, "Information",
                "context pack complete", Trim(summary)));
            return resp;
        }
        catch (Exception ex)
        {
            Fail("build_memory_context_pack", request.RequestId, request.TaskId, ex);
            throw;
        }
    }

    [McpServerTool(Name = "search_knowledge")]
    public async ValueTask<SearchKnowledgeResponse> SearchKnowledge(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken)
    {
        Start("search_knowledge", request.RequestId, null);
        try
        {
            var resp = await searchService.SearchKnowledgeAsync(request, cancellationToken).ConfigureAwait(false);
            var summary = JsonSerializer.Serialize(new
            {
                queryKind = request.QueryKind.ToString(),
                final = resp.Diagnostics.FinalCandidateCount,
                lexical = resp.Diagnostics.LexicalCandidateCount,
                vector = resp.Diagnostics.VectorCandidateCount,
                elapsedMs = resp.Diagnostics.ElapsedMs
            });
            monitorEventSink.Publish(new MonitorEventDto(
                0, DateTimeOffset.UtcNow, MonitorEventKind.RequestSuccess, request.RequestId,
                "search_knowledge", null, "Information",
                $"final={resp.Diagnostics.FinalCandidateCount}", Trim(summary)));
            return resp;
        }
        catch (Exception ex)
        {
            Fail("search_knowledge", request.RequestId, null, ex);
            throw;
        }
    }

    [McpServerTool(Name = "get_knowledge_item")]
    public async ValueTask<GetKnowledgeItemResponse> GetKnowledgeItem(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken)
    {
        Start("get_knowledge_item", request.RequestId, null);
        try
        {
            var resp = await readService.GetKnowledgeItemAsync(request, cancellationToken).ConfigureAwait(false);
            var summary = JsonSerializer.Serialize(new
            {
                itemId = request.KnowledgeItemId,
                relations = resp.Relations.Count,
                segments = resp.Segments.Count
            });
            monitorEventSink.Publish(new MonitorEventDto(
                0, DateTimeOffset.UtcNow, MonitorEventKind.RequestSuccess, request.RequestId,
                "get_knowledge_item", null, "Information",
                $"segments={resp.Segments.Count} relations={resp.Relations.Count}", Trim(summary)));
            return resp;
        }
        catch (Exception ex)
        {
            Fail("get_knowledge_item", request.RequestId, null, ex);
            throw;
        }
    }

    [McpServerTool(Name = "get_related_knowledge")]
    public async ValueTask<GetRelatedKnowledgeResponse> GetRelatedKnowledge(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken)
    {
        Start("get_related_knowledge", request.RequestId, null);
        try
        {
            var resp = await relatedService.GetRelatedKnowledgeAsync(request, cancellationToken).ConfigureAwait(false);
            var summary = JsonSerializer.Serialize(new
            {
                root = request.KnowledgeItemId,
                relationTypes = request.RelationTypes.Count,
                returned = resp.Items.Count
            });
            monitorEventSink.Publish(new MonitorEventDto(
                0, DateTimeOffset.UtcNow, MonitorEventKind.RequestSuccess, request.RequestId,
                "get_related_knowledge", null, "Information",
                $"items={resp.Items.Count}", Trim(summary)));
            return resp;
        }
        catch (Exception ex)
        {
            Fail("get_related_knowledge", request.RequestId, null, ex);
            throw;
        }
    }

    [McpServerTool(Name = "get_server_info")]
    public ServerInfoResponse GetServerInfo()
    {
        Start("get_server_info", null, null);
        var resp = appInfoProvider.GetServerInfo();
        var summary = JsonSerializer.Serialize(new { name = resp.ServerName, version = resp.ServerVersion, mode = resp.ProtocolMode });
        monitorEventSink.Publish(new MonitorEventDto(
            0, DateTimeOffset.UtcNow, MonitorEventKind.RequestSuccess, null,
            "get_server_info", null, "Information",
            "server info", Trim(summary)));
        return resp;
    }
}
