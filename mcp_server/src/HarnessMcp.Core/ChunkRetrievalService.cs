using System.Diagnostics;
using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class ChunkRetrievalService(
    IRequestValidator validator,
    IChunkQueryPlanner planner,
    IKnowledgeSearchService search,
    ISearchRequestContextStore? contextStore = null) : IChunkRetrievalService
{
    public async ValueTask<RetrieveMemoryByChunksResponse> RetrieveMemoryByChunksAsync(
        RetrieveMemoryByChunksRequest request,
        CancellationToken cancellationToken)
    {
        validator.Validate(request);
        var sw = Stopwatch.StartNew();
        var results = new List<ChunkRetrievalResultDto>();
        var notes = new List<string>
        {
            "Per-chunk independent retrieval",
            "Active-only enforced by search"
        };
        foreach (var chunk in request.RetrievalChunks)
        {
            var suffix = chunk.ChunkId;
            var searchReq = planner.BuildSearchRequest(request, chunk, suffix);

            // Store MCP-owned request context for this search request
            if (contextStore != null)
            {
                var queryKind = MapToQueryKind(chunk.ChunkType);
                var context = new SearchRequestContext(
                    TaskId: request.TaskId,
                    ChunkId: chunk.ChunkId,
                    ChunkType: MapChunkTypeToString(chunk.ChunkType),
                    RetrievalRoleHint: queryKind.ToString(),
                    TaskShape: chunk.TaskShape,
                    StructuredScopes: chunk.StructuredScopes,
                    Purpose: "harness_retrieval");
                contextStore.Set(searchReq.RequestId, context);
            }

            try
            {
                var searchResp = await search.SearchKnowledgeAsync(searchReq, cancellationToken).ConfigureAwait(false);
                var diag = searchResp.Diagnostics;
                if (string.Equals(diag.EmbeddingRoleUsed, "semantic-disabled", StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add($"chunk:{chunk.ChunkId} semantic disabled: noop provider");
                }
                else if (string.Equals(diag.EmbeddingRoleUsed, "lexical-only", StringComparison.OrdinalIgnoreCase))
                {
                    var modelDiag = diag.QueryEmbeddingModel ?? string.Empty;
                    const string prefix = "lexical-only:fallback:";
                    var detail = modelDiag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        ? modelDiag[prefix.Length..]
                        : "compatibility-failed";
                    detail = detail.Replace('-', ' ');
                    notes.Add($"chunk:{chunk.ChunkId} semantic degraded to lexical-only: {detail}");
                }
                else if (SemanticStateFormatting.IsSemanticActiveDegraded(diag.QueryEmbeddingModel))
                {
                    var signal = SemanticStateFormatting.TryGetFirstDegradationSignal(diag.QueryEmbeddingModel);
                    var detail = SemanticStateFormatting.FormatDegradedSignalForNote(signal);
                    notes.Add($"chunk:{chunk.ChunkId} semantic active with degraded quality: {detail}");
                }

                var tagged = searchResp.Candidates
                    .Select(c => c with { SupportedByChunks = new[] { chunk.ChunkId } })
                    .ToList();
                var bucket = ChunkBuckets.FromCandidates(tagged);
                results.Add(new ChunkRetrievalResultDto(chunk.ChunkId, chunk.ChunkType, bucket, searchResp.Diagnostics));
            }
            finally
            {
                // Remove context after search completes
                if (contextStore != null)
                {
                    contextStore.Remove(searchReq.RequestId);
                }
            }
        }

        sw.Stop();
        return new RetrieveMemoryByChunksResponse(
            request.SchemaVersion,
            "retrieve_memory_by_chunks",
            request.RequestId,
            request.TaskId,
            results,
            Notes: notes,
            sw.ElapsedMilliseconds);
    }

    private static QueryKind MapToQueryKind(ChunkType type) => type switch
    {
        ChunkType.CoreTask => QueryKind.CoreTask,
        ChunkType.Constraint => QueryKind.Constraint,
        ChunkType.Risk => QueryKind.Risk,
        ChunkType.Pattern => QueryKind.Pattern,
        ChunkType.SimilarCase => QueryKind.SimilarCase,
        _ => QueryKind.CoreTask
    };

    private static string MapChunkTypeToString(ChunkType type) => type switch
    {
        ChunkType.CoreTask => "core_task",
        ChunkType.Constraint => "constraint",
        ChunkType.Risk => "risk",
        ChunkType.Pattern => "pattern",
        ChunkType.SimilarCase => "similar_case",
        _ => "unknown"
    };
}
