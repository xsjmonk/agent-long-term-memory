using System;
using System.Collections.Generic;

namespace HarnessMcp.Core;

public static class SemanticStateFormatting
{
    private const string SemanticActiveDegradedPrefix = "semantic-active:degraded:";

    public static bool IsSemanticActiveDegraded(string? queryEmbeddingModel) =>
        !string.IsNullOrWhiteSpace(queryEmbeddingModel) &&
        queryEmbeddingModel.StartsWith(SemanticActiveDegradedPrefix, StringComparison.OrdinalIgnoreCase);

    public static string SemanticActiveDegraded(string modelName, IReadOnlyList<string> signals)
    {
        // Keep the top-level delimiter ":" so the harness can quickly detect semantic-active:degraded.
        // Join signals with "|" to preserve any internal ":" characters inside signal slugs.
        var signalPart = signals is { Count: > 0 }
            ? string.Join("|", signals)
            : "compatible-degraded";

        return $"{SemanticActiveDegradedPrefix}{modelName}:{signalPart}";
    }

    public static string? TryGetFirstDegradationSignal(string? queryEmbeddingModel)
    {
        if (!IsSemanticActiveDegraded(queryEmbeddingModel))
            return null;

        var rest = queryEmbeddingModel![SemanticActiveDegradedPrefix.Length..];
        // rest = "{modelName}:{signal1|signal2...}"
        var colon = rest.IndexOf(':');
        if (colon < 0)
            return null;

        var signalsPart = rest[(colon + 1)..];
        var first = signalsPart.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return first.Length > 0 ? first[0] : null;
    }

    public static string FormatDegradedSignalForNote(string? signalSlug)
    {
        if (string.IsNullOrWhiteSpace(signalSlug))
            return "semantic quality degraded";

        // Required human-readable note details (cheap and deterministic).
        return signalSlug switch
        {
            "text-truncated-before-embedding" => "text truncated before embedding",
            "text-processing-mismatch" => "text-processing-mismatch",
            "vector-space-mismatch" => "vector-space mismatch",
            "builder-warning:builder-warning" => "builder warning",
            "builder-warning:hashing-fallback-active" => "builder warning hashing fallback active",
            "missing-stored-normalize-metadata" => "missing stored normalize metadata",
            "missing-stored-text-processing-id" => "missing stored text-processing id",
            "missing-stored-vector-space-id" => "missing stored vector-space id",
            _ => signalSlug.Replace('-', ' ')
        };
    }
}

