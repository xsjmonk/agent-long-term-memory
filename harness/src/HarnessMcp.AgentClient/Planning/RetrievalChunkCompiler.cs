using System.Security.Cryptography;
using System.Text;
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

        // 1) core_task
        {
            var amb = intent.Ambiguities.Count > 0 ? $"|ambiguities:{string.Join(';', intent.Ambiguities)}" : string.Empty;
            var domainPart = string.IsNullOrWhiteSpace(intent.Domain) ? string.Empty : $"|domain:{intent.Domain}";
            var modulePart = string.IsNullOrWhiteSpace(intent.Module) ? string.Empty : $"|module:{intent.Module}";
            var featurePart = string.IsNullOrWhiteSpace(intent.Feature) ? string.Empty : $"|feature:{intent.Feature}";
            var goalPart = $"|goal:{intent.Goal}";
            var text = $"core_task|task_type:{intent.TaskType}{domainPart}{modulePart}{featurePart}{goalPart}{amb}";
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
            var text = $"constraint|{hc}";
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
            var text = $"risk|{r}";
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
                var text = $"pattern|{op}";
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

            var likelyLayers = scopes.Layers.Count > 0 ? scopes.Layers.ToArray() : intent.RetrievalFocuses.ToArray();
            var signature = new SimilarCaseSignature(
                TaskType: intent.TaskType,
                FeatureShape: intent.Feature ?? intent.Module ?? "unspecified",
                EngineChangeAllowed: engineChangeAllowed,
                LikelyLayers: likelyLayers,
                RiskSignals: intent.RiskSignals,
                Complexity: intent.Complexity);

            // Keep chunk.Text compact; the structured SimilarCase signature is carried separately.
            var likely = string.Join(",", signature.LikelyLayers.Take(4));
            var risk = string.Join(",", signature.RiskSignals.Take(4));
            var text =
                $"similar_case|task_type:{signature.TaskType}|feature_shape:{signature.FeatureShape}|engine_change_allowed:{signature.EngineChangeAllowed}|likely_layers:{likely}|risk_signals:{risk}|complexity:{signature.Complexity}";

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

