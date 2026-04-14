using System.Diagnostics;
using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class ChunkRetrievalService(
    IRequestValidator validator,
    IChunkQueryPlanner planner,
    IKnowledgeSearchService search) : IChunkRetrievalService
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
}
