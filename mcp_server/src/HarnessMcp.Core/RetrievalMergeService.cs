using System.Diagnostics;
using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class RetrievalMergeService(IRequestValidator validator) : IRetrievalMergeService
{
    public ValueTask<MergeRetrievalResultsResponse> MergeRetrievalResultsAsync(
        MergeRetrievalResultsRequest request,
        CancellationToken cancellationToken)
    {
        validator.Validate(request);
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();

        var decisions = MergeBucket(Collect(request.Retrieved, c => c.Results.Decisions), warnings);
        var constraints = MergeBucket(Collect(request.Retrieved, c => c.Results.Constraints), warnings);
        var best = MergeBucket(Collect(request.Retrieved, c => c.Results.BestPractices), warnings);
        var anti = MergeBucket(Collect(request.Retrieved, c => c.Results.Antipatterns), warnings);
        var similar = MergeBucket(Collect(request.Retrieved, c => c.Results.SimilarCases), warnings);
        var refs = MergeBucket(Collect(request.Retrieved, c => c.Results.References), warnings);
        var structures = MergeBucket(Collect(request.Retrieved, c => c.Results.Structures), warnings);

        sw.Stop();
        return ValueTask.FromResult(new MergeRetrievalResultsResponse(
            request.SchemaVersion,
            "merge_retrieval_results",
            request.RequestId,
            request.TaskId,
            decisions,
            constraints,
            best,
            anti,
            similar,
            refs,
            structures,
            warnings,
            sw.ElapsedMilliseconds));
    }

    private static IEnumerable<(string ChunkId, ChunkType ChunkType, KnowledgeCandidateDto Item)> Collect(
        RetrieveMemoryByChunksResponse retrieved,
        Func<ChunkRetrievalResultDto, IReadOnlyList<KnowledgeCandidateDto>> selector)
    {
        foreach (var cr in retrieved.ChunkResults)
        foreach (var item in selector(cr))
            yield return (cr.ChunkId, cr.ChunkType, item);
    }

    private static List<MergedKnowledgeItemDto> MergeBucket(
        IEnumerable<(string ChunkId, ChunkType ChunkType, KnowledgeCandidateDto Item)> rows,
        List<string> warnings)
    {
        var map = new Dictionary<Guid, MergedKnowledgeItemDto>();
        foreach (var (chunkId, chunkType, item) in rows)
        {
            if (!map.TryGetValue(item.KnowledgeItemId, out var existing))
            {
                map[item.KnowledgeItemId] = new MergedKnowledgeItemDto(
                    item,
                    [chunkId],
                    [chunkType],
                    ["first occurrence"]);
                continue;
            }

            var chosen = ChooseCanonical(existing.Item, item);
            if (chosen.Authority != existing.Item.Authority && chosen.KnowledgeItemId == item.KnowledgeItemId)
                warnings.Add("higher authority variant kept");

            var chunks = existing.SupportedByChunkIds.Append(chunkId).Distinct(StringComparer.Ordinal).ToList();
            var types = existing.SupportedByChunkTypes.Append(chunkType).Distinct().ToList();
            var rationales = existing.MergeRationales.Append("duplicate merged").ToList();
            map[item.KnowledgeItemId] = new MergedKnowledgeItemDto(chosen, chunks, types, rationales);
        }

        return map.Values
            .OrderByDescending(m => m.Item.Authority)
            .ThenByDescending(m => m.Item.FinalScore)
            .ThenByDescending(m => m.Item.SemanticScore + m.Item.LexicalScore)
            .ThenBy(m => m.Item.KnowledgeItemId)
            .ToList();
    }

    private static KnowledgeCandidateDto ChooseCanonical(KnowledgeCandidateDto a, KnowledgeCandidateDto b)
    {
        if (a.Authority != b.Authority)
            return a.Authority >= b.Authority ? a : b;
        if (!a.FinalScore.Equals(b.FinalScore))
            return a.FinalScore >= b.FinalScore ? a : b;
        var recA = a.SemanticScore + a.LexicalScore;
        var recB = b.SemanticScore + b.LexicalScore;
        if (!recA.Equals(recB))
            return recA >= recB ? a : b;
        return a.KnowledgeItemId.CompareTo(b.KnowledgeItemId) <= 0 ? a : b;
    }
}
