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
        var requireCheck = config.RequireCompatibilityCheck;
        var degradationSignals = new List<string>(capacity: 4);

        static IReadOnlyList<string> EmptySignals() => Array.Empty<string>();

        void AddDegradation(string signal) => degradationSignals.Add(signal);

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
            if (!requireCheck)
            {
                CollectBuilderDegradationSignals(query, AddDegradation);
                
                if (degradationSignals.Count > 0)
                    return new EmbeddingCompatibilityResult(
                        IsCompatible: true,
                        AllowLexicalFallback: allowLexicalFallback,
                        SemanticQualityDegraded: true,
                        Reason: "compatible:degraded:missing-stored-metadata",
                        DegradationSignals: degradationSignals.ToArray());

                return new EmbeddingCompatibilityResult(
                    IsCompatible: true,
                    AllowLexicalFallback: allowLexicalFallback,
                    SemanticQualityDegraded: false,
                    Reason: "compatible:missing-stored-metadata-but-check-disabled",
                    DegradationSignals: EmptySignals());
            }

            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:missing-stored-metadata",
                DegradationSignals: EmptySignals());
        }

        if (requireCheck)
            return CheckWithRequiredCompatibility(query, stored, allowLexicalFallback, degradationSignals, AddDegradation);

        return CheckWithoutRequiredCompatibility(query, stored, allowLexicalFallback, degradationSignals, AddDegradation);
    }

    private static EmbeddingCompatibilityResult CheckWithRequiredCompatibility(
        QueryEmbeddingResult query,
        StoredEmbeddingMetadata stored,
        bool allowLexicalFallback,
        List<string> degradationSignals,
        Action<string> addDegradation)
    {
        if (stored.Dimension != query.Dimension)
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: $"incompatible:dimension-mismatch:db={stored.Dimension} query={query.Dimension}",
                DegradationSignals: Array.Empty<string>());

        if (string.IsNullOrWhiteSpace(stored.ModelName))
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:missing-stored-model-name",
                DegradationSignals: Array.Empty<string>());

        if (!string.Equals(stored.ModelName, query.ModelName, StringComparison.OrdinalIgnoreCase))
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: $"incompatible:model-mismatch:db={stored.ModelName} query={query.ModelName}",
                DegradationSignals: Array.Empty<string>());

        if (stored.ModelVersion is not null && query.ModelVersion is not null &&
            !string.Equals(stored.ModelVersion, query.ModelVersion, StringComparison.OrdinalIgnoreCase))
        {
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: $"incompatible:model-version-mismatch:db={stored.ModelVersion} query={query.ModelVersion}",
                DegradationSignals: Array.Empty<string>());
        }

        if (!stored.NormalizeEmbeddings.HasValue)
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:missing-stored-normalize-metadata",
                DegradationSignals: Array.Empty<string>());

        if (query.NormalizeEmbeddings != stored.NormalizeEmbeddings.Value)
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:normalize-mismatch",
                DegradationSignals: Array.Empty<string>());

        if (string.IsNullOrWhiteSpace(stored.TextProcessingId))
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:missing-stored-text-processing-id",
                DegradationSignals: Array.Empty<string>());

        if (!string.Equals(stored.TextProcessingId, query.TextProcessingId, StringComparison.OrdinalIgnoreCase))
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:text-processing-mismatch",
                DegradationSignals: Array.Empty<string>());

        if (string.IsNullOrWhiteSpace(stored.VectorSpaceId))
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:missing-stored-vector-space-id",
                DegradationSignals: Array.Empty<string>());

        if (!string.Equals(stored.VectorSpaceId, query.VectorSpaceId, StringComparison.OrdinalIgnoreCase))
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:vector-space-mismatch",
                DegradationSignals: Array.Empty<string>());

        CollectBuilderDegradationSignals(query, addDegradation);

        var hasDegradation = degradationSignals.Count > 0;

        return new EmbeddingCompatibilityResult(
            IsCompatible: true,
            AllowLexicalFallback: allowLexicalFallback,
            SemanticQualityDegraded: hasDegradation,
            Reason: hasDegradation
                ? $"compatible:stored-role={stored.SelectedEmbeddingRole}:compatible:degraded"
                : $"compatible:stored-role={stored.SelectedEmbeddingRole}",
            DegradationSignals: hasDegradation ? degradationSignals.ToArray() : Array.Empty<string>());
    }

    private static EmbeddingCompatibilityResult CheckWithoutRequiredCompatibility(
        QueryEmbeddingResult query,
        StoredEmbeddingMetadata stored,
        bool allowLexicalFallback,
        List<string> degradationSignals,
        Action<string> addDegradation)
    {
        if (stored.Dimension != query.Dimension)
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: $"incompatible:dimension-mismatch:db={stored.Dimension} query={query.Dimension}",
                DegradationSignals: Array.Empty<string>());

        if (string.IsNullOrWhiteSpace(stored.ModelName))
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: "incompatible:missing-stored-model-name",
                DegradationSignals: Array.Empty<string>());

        if (!string.Equals(stored.ModelName, query.ModelName, StringComparison.OrdinalIgnoreCase))
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: $"incompatible:model-mismatch:db={stored.ModelName} query={query.ModelName}",
                DegradationSignals: Array.Empty<string>());

        if (stored.ModelVersion is not null && query.ModelVersion is not null &&
            !string.Equals(stored.ModelVersion, query.ModelVersion, StringComparison.OrdinalIgnoreCase))
        {
            return new EmbeddingCompatibilityResult(
                IsCompatible: false,
                AllowLexicalFallback: allowLexicalFallback,
                SemanticQualityDegraded: false,
                Reason: $"incompatible:model-version-mismatch:db={stored.ModelVersion} query={query.ModelVersion}",
                DegradationSignals: Array.Empty<string>());
        }

        CollectDegradationSignalsWhenCheckDisabled(query, stored, addDegradation);

        var hasDegradation = degradationSignals.Count > 0;

        return new EmbeddingCompatibilityResult(
            IsCompatible: true,
            AllowLexicalFallback: allowLexicalFallback,
            SemanticQualityDegraded: hasDegradation,
            Reason: hasDegradation
                ? $"compatible:degraded:stored-role={stored.SelectedEmbeddingRole}"
                : $"compatible:stored-role={stored.SelectedEmbeddingRole}",
            DegradationSignals: hasDegradation ? degradationSignals.ToArray() : Array.Empty<string>());
    }

    private static bool HasBuilderDegradationSignals(QueryEmbeddingResult query)
    {
        return query.Truncated || query.Warnings.Count > 0;
    }

    private static void CollectBuilderDegradationSignals(
        QueryEmbeddingResult query,
        Action<string> addDegradation)
    {
        if (query.Truncated)
            addDegradation("text-truncated-before-embedding");

        if (query.Warnings.Count > 0)
        {
            var first = query.Warnings[0];
            var isHashing = first.Contains("hashing", StringComparison.OrdinalIgnoreCase) &&
                                first.Contains("fallback", StringComparison.OrdinalIgnoreCase);
            addDegradation(isHashing ? "builder-warning:hashing-fallback-active" : "builder-warning:builder-warning");
        }
    }

    private static void CollectDegradationSignalsWhenCheckDisabled(
        QueryEmbeddingResult query,
        StoredEmbeddingMetadata stored,
        Action<string> addDegradation)
    {
        CollectBuilderDegradationSignals(query, addDegradation);

        if (!stored.NormalizeEmbeddings.HasValue)
            addDegradation("missing-stored-normalize-metadata");

        if (string.IsNullOrWhiteSpace(stored.TextProcessingId))
            addDegradation("missing-stored-text-processing-id");

        if (string.IsNullOrWhiteSpace(stored.VectorSpaceId))
            addDegradation("missing-stored-vector-space-id");

        if (!string.IsNullOrWhiteSpace(stored.TextProcessingId) &&
            !string.Equals(stored.TextProcessingId, query.TextProcessingId, StringComparison.OrdinalIgnoreCase))
            addDegradation("text-processing-mismatch");

        if (!string.IsNullOrWhiteSpace(stored.VectorSpaceId) &&
            !string.Equals(stored.VectorSpaceId, query.VectorSpaceId, StringComparison.OrdinalIgnoreCase))
            addDegradation("vector-space-mismatch");
    }
}