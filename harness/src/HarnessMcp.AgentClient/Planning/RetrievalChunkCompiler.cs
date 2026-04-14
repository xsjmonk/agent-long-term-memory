using System.Security.Cryptography;
using System.Text;
using System.Linq;
using HarnessMcp.Contracts;
using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Planning;

public sealed class RetrievalChunkCompiler
{
    private readonly ScopeInferenceService _scopeInferenceService;
    private readonly ChunkTextNormalizer _textNormalizer;

    public RetrievalChunkCompiler(
        ScopeInferenceService scopeInferenceService,
        ChunkTextNormalizer textNormalizer)
    {
        _scopeInferenceService = scopeInferenceService;
        _textNormalizer = textNormalizer;
    }

    public RetrievalChunkSet Compile(RequirementIntent intent)
    {
        var scopes = _scopeInferenceService.Infer(intent);
        var chunks = new List<RetrievalChunk>();

        static string MakeChunkId(string sessionId, string taskId, string purpose)
        {
            var seed = $"{sessionId}:{taskId}:{purpose}";
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes(seed));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        string Norm(string? input) => _textNormalizer.Normalize(input);

        static bool HasText(string? s) => !string.IsNullOrWhiteSpace(s);

        static string TrimToSingleSpace(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

        // 1) core_task
        {
            // Natural compact retrieval text (no pseudo-schema markers).
            // Preferred order: feature, module, goal (only if feature/module absent), then domain only when needed.
            var parts = new List<string>();
            if (HasText(intent.Feature)) parts.Add(Norm(intent.Feature));
            if (HasText(intent.Module)) parts.Add(Norm(intent.Module));

            if (parts.Count == 0)
                parts.Add(Norm(intent.Goal));

            if (parts.Count <= 1 && HasText(intent.Domain))
            {
                // Keep domain only for clarity.
                parts.Add("in " + Norm(intent.Domain));
            }

            var text = TrimToSingleSpace(string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))));
            chunks.Add(new RetrievalChunk(
                ChunkId: MakeChunkId(intent.SessionId, intent.TaskId, "core_task"),
                ChunkType: ChunkType.CoreTask,
                Text: _textNormalizer.Normalize(text),
                Scopes: scopes,
                SimilarCase: null));
        }

        // 2) constraint
        foreach (var hc in intent.HardConstraints)
        {
            if (string.IsNullOrWhiteSpace(hc))
                continue;
            var text = Norm(hc);
            chunks.Add(new RetrievalChunk(
                ChunkId: MakeChunkId(intent.SessionId, intent.TaskId, "constraint:" + hc),
                ChunkType: ChunkType.Constraint,
                Text: _textNormalizer.Normalize(text),
                Scopes: scopes,
                SimilarCase: null));
        }

        // 3) risk
        foreach (var r in intent.RiskSignals)
        {
            if (string.IsNullOrWhiteSpace(r))
                continue;
            var text = Norm(r);
            chunks.Add(new RetrievalChunk(
                ChunkId: MakeChunkId(intent.SessionId, intent.TaskId, "risk:" + r),
                ChunkType: ChunkType.Risk,
                Text: _textNormalizer.Normalize(text),
                Scopes: scopes,
                SimilarCase: null));
        }

        // 4) pattern
        var styleCueOps = intent.RequestedOperations.Count > 0
            ? intent.RequestedOperations
            : (intent.SoftConstraints.Count > 0 ? intent.SoftConstraints : Array.Empty<string>());

        if (styleCueOps.Count > 0)
        {
            foreach (var op in styleCueOps)
            {
                if (string.IsNullOrWhiteSpace(op))
                    continue;
                var text = Norm(op);
                chunks.Add(new RetrievalChunk(
                    ChunkId: MakeChunkId(intent.SessionId, intent.TaskId, "pattern:" + op),
                    ChunkType: ChunkType.Pattern,
                    Text: _textNormalizer.Normalize(text),
                    Scopes: scopes,
                    SimilarCase: null));
            }
        }

        // 5) similar_case for medium/high
        var complexityKey = intent.Complexity.Trim().ToLowerInvariant();
        if (string.Equals(complexityKey, "medium", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(complexityKey, "high", StringComparison.OrdinalIgnoreCase))
        {
            var engineChangeAllowed = intent.HardConstraints.Any(h =>
                !string.IsNullOrWhiteSpace(h) &&
                h.Contains("engine", StringComparison.OrdinalIgnoreCase) &&
                h.Contains("change", StringComparison.OrdinalIgnoreCase) &&
                (h.Contains("allowed", StringComparison.OrdinalIgnoreCase) ||
                 h.Contains("permit", StringComparison.OrdinalIgnoreCase)));

            var likelyLayers = scopes.Layers.Count > 0 ? scopes.Layers.ToArray() : intent.CandidateLayers.ToArray();
            var signature = new SimilarCaseSignature(
                TaskType: intent.TaskType,
                FeatureShape: intent.Feature ?? intent.Module ?? "unspecified",
                EngineChangeAllowed: engineChangeAllowed,
                LikelyLayers: likelyLayers,
                RiskSignals: intent.RiskSignals,
                Complexity: intent.Complexity);

            // Natural compact query derived from signature.
            var text = SimilarCaseQueryTextBuilder.Build(signature);

            chunks.Add(new RetrievalChunk(
                ChunkId: MakeChunkId(intent.SessionId, intent.TaskId, "similar_case"),
                ChunkType: ChunkType.SimilarCase,
                Text: _textNormalizer.Normalize(text),
                Scopes: scopes,
                SimilarCase: signature));
        }

        var styleCueExist = styleCueOps.Count > 0;
        var hasSimilar = string.Equals(complexityKey, "medium", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(complexityKey, "high", StringComparison.OrdinalIgnoreCase);

        var coverage = new ChunkCoverageReport(
            HasCoreTask: true,
            HasConstraint: intent.HardConstraints.Count > 0,
            HasRisk: intent.RiskSignals.Count > 0,
            HasPattern: styleCueExist,
            HasSimilarCase: hasSimilar);

        return new RetrievalChunkSet(
            SessionId: intent.SessionId,
            TaskId: intent.TaskId,
            Complexity: intent.Complexity,
            Chunks: chunks,
            CoverageReport: coverage);
    }
}

