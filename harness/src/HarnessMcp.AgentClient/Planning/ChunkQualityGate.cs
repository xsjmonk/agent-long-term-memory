using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Planning;

public sealed class ChunkQualityGate
{
    public const int MaxChunkTextChars = 200;
    private static readonly string[] ForbiddenProtocolMarkers =
    [
        "core_task|",
        "constraint|",
        "risk|",
        "pattern|",
        "similar_case|",
        "task_type:",
        "goal:",
        "ambiguities:"
    ];

    public ChunkQualityReport Validate(RetrievalChunkSet chunkSet, RequirementIntent intent)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (chunkSet.Chunks is null || chunkSet.Chunks.Count == 0)
            errors.Add("Chunk set must contain at least one chunk.");

        // Validate scope completeness: lists must be non-null (we construct them in the compiler).
        bool ScopeOk(PlannedChunkScopes s)
        {
            return s.Features is not null &&
                   s.Layers is not null &&
                   s.Concerns is not null &&
                   s.Repos is not null &&
                   s.Services is not null &&
                   s.Symbols is not null;
        }

        foreach (var c in chunkSet.Chunks)
        {
            if (c.Text is null) errors.Add($"Chunk {c.ChunkId} has null text.");
            if (string.IsNullOrWhiteSpace(c.Text)) errors.Add($"Chunk {c.ChunkId} has empty text.");
            if (c.Scopes is null) errors.Add($"Chunk {c.ChunkId} has null scopes.");
            else if (!ScopeOk(c.Scopes))
                errors.Add($"Chunk {c.ChunkId} scopes are missing required lists.");

            if (c.Text is not null && c.Text.Length > MaxChunkTextChars)
                errors.Add($"Chunk {c.ChunkId} text too long ({c.Text.Length} chars > {MaxChunkTextChars}).");

            // Protocol purity: the retrieval key text must not contain any pseudo-protocol markers.
            foreach (var marker in ForbiddenProtocolMarkers)
            {
                if (!string.IsNullOrWhiteSpace(c.Text) &&
                    c.Text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    errors.Add($"Chunk {c.ChunkId} text contains forbidden protocol marker '{marker}'.");
                    break;
                }
            }
        }

        // Mandatory coverage based on intent.
        if (!chunkSet.CoverageReport.HasCoreTask)
            errors.Add("CoverageReport missing core_task chunk.");

        if (intent.HardConstraints.Count > 0 && !chunkSet.CoverageReport.HasConstraint)
            errors.Add("HardConstraints present but no constraint chunk was emitted.");

        if (intent.RiskSignals.Count > 0 && !chunkSet.CoverageReport.HasRisk)
            errors.Add("RiskSignals present but no risk chunk was emitted.");

        var styleCueOps = intent.RequestedOperations.Count > 0 || intent.SoftConstraints.Count > 0;
        if (styleCueOps && !chunkSet.CoverageReport.HasPattern)
            errors.Add("Style cues present but no pattern chunk was emitted.");

        var complexityKey = intent.Complexity.Trim().ToLowerInvariant();
        var needsSimilar = complexityKey is "medium" or "high";
        if (needsSimilar && !chunkSet.CoverageReport.HasSimilarCase)
            errors.Add("Complexity is medium/high but similar_case chunk was not emitted.");

        var isValid = errors.Count == 0;
        var hasSimilar = chunkSet.CoverageReport.HasSimilarCase;
        return new ChunkQualityReport(
            IsValid: isValid,
            HasCoreTask: chunkSet.CoverageReport.HasCoreTask,
            HasConstraint: chunkSet.CoverageReport.HasConstraint,
            HasRisk: chunkSet.CoverageReport.HasRisk,
            HasPattern: chunkSet.CoverageReport.HasPattern,
            HasSimilarCase: hasSimilar,
            Errors: errors,
            Warnings: warnings);
    }
}

