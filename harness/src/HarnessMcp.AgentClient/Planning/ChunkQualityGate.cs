using System.Text.RegularExpressions;
using HarnessMcp.Contracts;
using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Planning;

public sealed class ChunkQualityGate
{
    public const int MaxChunkTextChars = 200;

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

            // Purity: each chunk text must start with the expected purpose prefix.
            var expectedPrefix = c.ChunkType switch
            {
                ChunkType.CoreTask => "core_task|",
                ChunkType.Constraint => "constraint|",
                ChunkType.Risk => "risk|",
                ChunkType.Pattern => "pattern|",
                ChunkType.SimilarCase => "similar_case|",
                _ => null
            };

            if (expectedPrefix is null)
                errors.Add($"Chunk {c.ChunkId} has unknown ChunkType '{c.ChunkType}'.");
            else if (!c.Text!.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Chunk {c.ChunkId} purity error: expected prefix '{expectedPrefix}' for type '{c.ChunkType}'.");

            // No mixed-purpose text: the chunk text must not contain other purpose prefixes.
            var mixedPurposes = new[]
            {
                ("core_task|", ChunkType.CoreTask),
                ("constraint|", ChunkType.Constraint),
                ("risk|", ChunkType.Risk),
                ("pattern|", ChunkType.Pattern),
                ("similar_case|", ChunkType.SimilarCase),
            };
            foreach (var (needle, _) in mixedPurposes)
            {
                if (!string.Equals(needle, expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
                    c.Text!.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    errors.Add($"Chunk {c.ChunkId} purity error: contains other purpose marker '{needle}'.");
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

        // Ambiguity preservation: if intent has ambiguities, require them to appear in the core_task chunk text.
        if (intent.Ambiguities.Count > 0)
        {
            var core = chunkSet.Chunks.FirstOrDefault(c => c.ChunkType == ChunkType.CoreTask);
            if (core is null)
                errors.Add("Ambiguity preservation failed: core_task chunk missing.");
            else
            {
                foreach (var a in intent.Ambiguities)
                {
                    var trimmed = a.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) &&
                        (core.Text is null || !core.Text.Contains(trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        errors.Add($"Ambiguity preservation failed: core_task chunk does not include ambiguity '{a}'.");
                        break;
                    }
                }
            }
        }

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

