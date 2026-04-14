using HarnessMcp.Core;
using HarnessMcp.Contracts;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class NoOpQueryEmbeddingService : IQueryEmbeddingService
{
    public ValueTask<QueryEmbeddingResult> EmbedAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new QueryEmbeddingResult(
            Vector: ReadOnlyMemory<float>.Empty,
            Provider: "noop",
            ModelName: "noop",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 0,
            FallbackMode: false,
            TextProcessingId: "noop",
            VectorSpaceId: "noop",
            InputCharCount: 0,
            EffectiveTextCharCount: 0,
            Truncated: false,
            Warnings: Array.Empty<string>()));
}
