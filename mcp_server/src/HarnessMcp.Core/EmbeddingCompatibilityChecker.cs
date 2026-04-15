using System;
using System.Collections.Generic;

namespace HarnessMcp.Core;

public sealed class EmbeddingCompatibilityChecker : IEmbeddingCompatibilityChecker
{
    public EmbeddingCompatibilityResult Check(
        QueryEmbeddingResult query,
        StoredEmbeddingMetadata? stored,
        Contracts.EmbeddingConfig config)
    {
        var allowLexicalFallback = config.AllowLexicalFallbackOnSemanticIncompatibility;
        var degradationSignals = new List<string>(capacity: 4);

        static IReadOnlyList<string> EmptySignals() => Array.Empty<string>();

        void AddDegradation(string signal) => degradationSignals.Add(signal);

        // ---- Hard compatibility blockers ----
        if (query.Vector.IsEmpty || query.Dimension <= 0)
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:empty-vector",
                DegradationSignals: EmptySignals());

        if (query.FallbackMode && !config.AllowHashingFallback)
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:hashing-fallback-disallowed",
                DegradationSignals: EmptySignals());

        if (stored is null || !stored.HasRows)
        {
            if (!config.RequireCompatibilityCheck)
            {
                // No hard metadata check, but we still allow degradation to be surfaced.
                var noMetadataIncompatibilityReason = CollectDegradationSignals(query, stored, config, AddDegradation);
                if (noMetadataIncompatibilityReason is not null)
                    return new EmbeddingCompatibilityResult(
                        IsCompatible: false,
                        AllowLexicalFallback: allowLexicalFallback,
                        SemanticQualityDegraded: false,
                        Reason: noMetadataIncompatibilityReason,
                        DegradationSignals: EmptySignals());

                var degraded = degradationSignals.Count > 0;
                return new EmbeddingCompatibilityResult(
                    IsCompatible: true,
                    AllowLexicalFallback: allowLexicalFallback,
                    SemanticQualityDegraded: degraded,
                    Reason: degraded ? "compatible:degraded:missing-stored-metadata" : "compatible:missing-stored-metadata-but-check-disabled",
                    DegradationSignals: degradationSignals.ToArray());
            }

            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:missing-stored-metadata",
                DegradationSignals: EmptySignals());
        }

        if (stored.Dimension != query.Dimension)
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: $"incompatible:dimension-mismatch:db={stored.Dimension} query={query.Dimension}",
                DegradationSignals: EmptySignals());

        if (!string.Equals(stored.ModelName, query.ModelName, StringComparison.OrdinalIgnoreCase))
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: $"incompatible:model-mismatch:db={stored.ModelName} query={query.ModelName}",
                DegradationSignals: EmptySignals());

        if (stored.ModelVersion is not null && query.ModelVersion is not null &&
            !string.Equals(stored.ModelVersion, query.ModelVersion, StringComparison.OrdinalIgnoreCase))
        {
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: $"incompatible:model-version-mismatch:db={stored.ModelVersion} query={query.ModelVersion}",
                DegradationSignals: EmptySignals());
        }

        if (stored.NormalizeEmbeddings is bool dbNorm && !query.NormalizeEmbeddings.Equals(dbNorm))
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:normalize-mismatch",
                DegradationSignals: EmptySignals());

        // ---- Semantic-quality degradation signals (non-blocking) ----
        var incompatibilityReason = CollectDegradationSignals(query, stored, config, AddDegradation);
        if (incompatibilityReason is not null)
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: incompatibilityReason,
                DegradationSignals: EmptySignals());

        var semanticQualityDegraded = degradationSignals.Count > 0;

        return new EmbeddingCompatibilityResult(
            IsCompatible: true,
            AllowLexicalFallback: allowLexicalFallback,
            SemanticQualityDegraded: semanticQualityDegraded,
            Reason: semanticQualityDegraded
                ? $"compatible:stored-role={stored.SelectedEmbeddingRole}:compatible:degraded"
                : $"compatible:stored-role={stored.SelectedEmbeddingRole}",
            DegradationSignals: degradationSignals.ToArray());
    }

    private static string? CollectDegradationSignals(
        QueryEmbeddingResult query,
        StoredEmbeddingMetadata? stored,
        Contracts.EmbeddingConfig config,
        Action<string> addDegradation)
    {
        if (query.Truncated)
            addDegradation("text-truncated-before-embedding");

        if (query.Warnings.Count > 0)
        {
            var first = query.Warnings[0];
            var isHashingFallback = first.Contains("hashing", StringComparison.OrdinalIgnoreCase) &&
                                    first.Contains("fallback", StringComparison.OrdinalIgnoreCase);
            addDegradation(isHashingFallback ? "builder-warning:hashing-fallback-active" : "builder-warning:builder-warning");
        }

        if (stored is null)
            return null;

        // Missing stored identity fields -> degradation signals
        if (!stored.NormalizeEmbeddings.HasValue)
            addDegradation("missing-stored-normalize-metadata");

        if (string.IsNullOrWhiteSpace(stored.TextProcessingId))
            addDegradation("missing-stored-text-processing-id");

        if (string.IsNullOrWhiteSpace(stored.VectorSpaceId))
            addDegradation("missing-stored-vector-space-id");

        // Compare text_processing_id only when stored metadata provides it
        if (!string.IsNullOrWhiteSpace(stored.TextProcessingId) &&
            !string.Equals(stored.TextProcessingId, query.TextProcessingId, StringComparison.OrdinalIgnoreCase))
        {
            addDegradation("text-processing-mismatch");
        }

        // Compare vector_space_id only when stored metadata provides it
        if (!string.IsNullOrWhiteSpace(stored.VectorSpaceId) &&
            !string.Equals(stored.VectorSpaceId, query.VectorSpaceId, StringComparison.OrdinalIgnoreCase))
        {
            addDegradation("vector-space-mismatch");
        }

        return null;
    }
}

