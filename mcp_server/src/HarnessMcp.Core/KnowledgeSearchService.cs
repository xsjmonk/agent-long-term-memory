using System.Diagnostics;
using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class KnowledgeSearchService(
    IRequestValidator validator,
    IScopeNormalizer scopeNormalizer,
    IKnowledgeRepository repository,
    IQueryEmbeddingService embeddingService,
    IHybridRankingService ranking,
    EmbeddingConfig embeddingConfig,
    IEmbeddingMetadataInspector metadataInspector,
    IEmbeddingCompatibilityChecker compatibilityChecker) : IKnowledgeSearchService
{
    public async ValueTask<SearchKnowledgeResponse> SearchKnowledgeAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken)
    {
        validator.Validate(request);
        var sw = Stopwatch.StartNew();
        var normalizedScopes = scopeNormalizer.Normalize(request.Scopes);
        var query = QueryTextNormalizer.Normalize(request.QueryText);
        var req = request with { QueryText = query, Scopes = normalizedScopes };

        var lexical = await repository.SearchLexicalAsync(req, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<KnowledgeCandidateDto> semantic = Array.Empty<KnowledgeCandidateDto>();

        var semanticProviderNoOp = string.Equals(
            embeddingConfig.QueryEmbeddingProvider,
            "NoOp",
            StringComparison.OrdinalIgnoreCase);

        var queryEmbeddingModelDiag = semanticProviderNoOp ? "semantic-disabled:noop" : embeddingConfig.Model;
        var embeddingRoleUsedDiag = semanticProviderNoOp ? "semantic-disabled" : req.QueryKind.ToString();

        if (!semanticProviderNoOp)
        {
            var embedResult = await embeddingService.EmbedAsync(req, cancellationToken).ConfigureAwait(false);
            var storedMetadata = await metadataInspector.GetMetadataForRoleAsync(req.QueryKind, cancellationToken).ConfigureAwait(false);
            var compat = compatibilityChecker.Check(embedResult, storedMetadata, embeddingConfig);

            if (compat.IsCompatible)
            {
                semantic = await repository.SearchSemanticAsync(req, embedResult.Vector, cancellationToken).ConfigureAwait(false);
                var selectedRole = storedMetadata?.SelectedEmbeddingRole ?? req.QueryKind.ToString();
                embeddingRoleUsedDiag = compat.SemanticQualityDegraded ? $"{selectedRole}|degraded" : selectedRole;

                queryEmbeddingModelDiag = compat.SemanticQualityDegraded
                    ? SemanticStateFormatting.SemanticActiveDegraded(embedResult.ModelName, compat.DegradationSignals)
                    : embedResult.ModelName;
            }
            else
            {
                if (!compat.AllowLexicalFallback)
                {
                    throw new InvalidOperationException($"Semantic search blocked by compatibility check: {compat.Reason}");
                }

                semantic = Array.Empty<KnowledgeCandidateDto>();
                embeddingRoleUsedDiag = "lexical-only";
                queryEmbeddingModelDiag = ToLexicalOnlyDiag(compat.Reason);
            }
        }

        var ranked = ranking.Rank(lexical, semantic, req);
        var top = ranked.Take(req.TopK).ToList();
        sw.Stop();

        var diag = new SearchKnowledgeDiagnosticsDto(
            lexical.Count,
            semantic.Count,
            ranked.Count,
            top.Count,
            sw.ElapsedMilliseconds,
            queryEmbeddingModelDiag,
            embeddingRoleUsedDiag);

        var response = new SearchKnowledgeResponse(
            req.SchemaVersion,
            "search_knowledge",
            req.RequestId,
            top,
            diag);

        return response;
    }

    private static string ToLexicalOnlyDiag(string compatibilityReason)
    {
        // compatibilityReason is expected to be like: "incompatible:<reason>"
        if (compatibilityReason.Contains("hashing-fallback-disallowed", StringComparison.OrdinalIgnoreCase))
            return "lexical-only:fallback:hashing-fallback-disallowed";
        if (compatibilityReason.Contains("model-mismatch", StringComparison.OrdinalIgnoreCase))
            return "lexical-only:fallback:model-mismatch";
        if (compatibilityReason.Contains("dimension-mismatch", StringComparison.OrdinalIgnoreCase))
            return "lexical-only:fallback:dimension-mismatch";
        if (compatibilityReason.Contains("text-processing-mismatch", StringComparison.OrdinalIgnoreCase))
            return "lexical-only:fallback:text-processing-mismatch";
        if (compatibilityReason.Contains("vector-space-mismatch", StringComparison.OrdinalIgnoreCase))
            return "lexical-only:fallback:vector-space-mismatch";
        return "lexical-only:fallback:compatibility-check-failed";
    }
}
