using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

internal static class ChunkBuckets
{
    public static ChunkBucketDto Empty { get; } = new(
        Array.Empty<KnowledgeCandidateDto>(),
        Array.Empty<KnowledgeCandidateDto>(),
        Array.Empty<KnowledgeCandidateDto>(),
        Array.Empty<KnowledgeCandidateDto>(),
        Array.Empty<KnowledgeCandidateDto>(),
        Array.Empty<KnowledgeCandidateDto>(),
        Array.Empty<KnowledgeCandidateDto>());

    public static ChunkBucketDto FromCandidates(IEnumerable<KnowledgeCandidateDto> candidates)
    {
        var list = candidates.ToList();
        return new ChunkBucketDto(
            list.Where(c => c.RetrievalClass == RetrievalClass.Decision).ToList(),
            list.Where(c => c.RetrievalClass == RetrievalClass.BestPractice).ToList(),
            list.Where(c => c.RetrievalClass == RetrievalClass.Antipattern).ToList(),
            list.Where(c => c.RetrievalClass == RetrievalClass.SimilarCase).ToList(),
            list.Where(c => c.RetrievalClass == RetrievalClass.Constraint).ToList(),
            list.Where(c => c.RetrievalClass == RetrievalClass.Reference).ToList(),
            list.Where(c => c.RetrievalClass == RetrievalClass.Structure).ToList());
    }
}
