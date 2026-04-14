using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class ContextPackAssembler : IContextPackAssembler
{
    public BuildMemoryContextPackResponse Assemble(
        BuildMemoryContextPackRequest request,
        MergeRetrievalResultsResponse merged,
        long assemblyElapsedMs)
    {
        var warnings = new List<string>();

        if (merged.Decisions.Count == 0)
            warnings.Add("empty decisions section");
        if (request.RequirementIntent.HardConstraints.Count > 0 && merged.Constraints.Count == 0)
            warnings.Add("empty constraints section while hard constraints present");

        var lowSupportSimilar = merged.SimilarCases.Count(m => m.SupportedByChunkIds.Count < 2);
        if (lowSupportSimilar > 0)
            warnings.Add($"low-support similar cases: {lowSupportSimilar}");

        foreach (var cr in request.Retrieved.ChunkResults)
        {
            var total = cr.Results.Decisions.Count + cr.Results.BestPractices.Count + cr.Results.Antipatterns.Count +
                        cr.Results.SimilarCases.Count + cr.Results.Constraints.Count + cr.Results.References.Count +
                        cr.Results.Structures.Count;
            if (total == 0)
                warnings.Add($"no results for chunk {cr.ChunkId}");
        }

        var section = new ContextPackSectionDto(
            merged.Decisions,
            merged.Constraints,
            merged.BestPractices,
            merged.AntiPatterns,
            merged.SimilarCases,
            merged.References,
            merged.Structures);

        var diag = new ContextPackDiagnosticsDto(
            request.Retrieved.ChunkResults.Count,
            DistinctCount(merged),
            request.Retrieved.ElapsedMs,
            merged.ElapsedMs,
            assemblyElapsedMs,
            warnings.Concat(merged.Warnings).ToList());

        return new BuildMemoryContextPackResponse(
            request.SchemaVersion,
            "build_memory_context_pack",
            request.RequestId,
            request.TaskId,
            section,
            diag);
    }

    private static int DistinctCount(MergeRetrievalResultsResponse merged)
    {
        var set = new HashSet<Guid>();
        foreach (var x in merged.Decisions) set.Add(x.Item.KnowledgeItemId);
        foreach (var x in merged.Constraints) set.Add(x.Item.KnowledgeItemId);
        foreach (var x in merged.BestPractices) set.Add(x.Item.KnowledgeItemId);
        foreach (var x in merged.AntiPatterns) set.Add(x.Item.KnowledgeItemId);
        foreach (var x in merged.SimilarCases) set.Add(x.Item.KnowledgeItemId);
        foreach (var x in merged.References) set.Add(x.Item.KnowledgeItemId);
        foreach (var x in merged.Structures) set.Add(x.Item.KnowledgeItemId);
        return set.Count;
    }
}
