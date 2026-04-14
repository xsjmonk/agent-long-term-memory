using HarnessMcp.AgentClient.Support;
using HarnessMcp.AgentClient.Transport;
using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Planning;

public sealed class FallbackRetrievalPlanner
{
    private readonly IMcpToolClient _mcp;
    private readonly McpRequestMapper _mapper;
    private readonly ScopeInferenceService _scopeInference;

    public FallbackRetrievalPlanner(IMcpToolClient mcp, McpRequestMapper mapper, ScopeInferenceService scopeInference)
    {
        _mcp = mcp;
        _mapper = mapper;
        _scopeInference = scopeInference;
    }

    public async Task<FallbackRetrievalResult> PlanAndRunFallbacksAsync(
        RequirementIntent intent,
        RetrievalChunkSet chunkSet,
        MergeRetrievalResultsResponse merged,
        AuthorityLevel minimumAuthority,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scopes = _scopeInference.Infer(intent);

        var fallbackSearches = new List<SearchKnowledgeResponse>();
        var hydratedItems = new List<GetKnowledgeItemResponse>();
        var diagnostics = new List<string>();
        bool usedAny = false;

        var complexityKey = intent.Complexity.Trim().ToLowerInvariant();
        bool needsSimilar = complexityKey is "medium" or "high";

        // Rule A: missing similar cases
        if (needsSimilar && merged.SimilarCases.Count == 0)
        {
            usedAny = true;
            var signatureChunk = chunkSet.Chunks.FirstOrDefault(c => c.ChunkType == ChunkType.SimilarCase && c.SimilarCase is not null);
            if (signatureChunk?.SimilarCase is not null)
            {
                var signature = signatureChunk.SimilarCase;
                var queryText = System.Text.Json.JsonSerializer.Serialize(signature, JsonHelpers.Default);

                var searchReq = _mapper.MapFallbackSearchRequest(
                    requestId: Ids.NewRequestId(intent.TaskId) + ":fallback-similar",
                    taskId: intent.TaskId,
                    kind: QueryKind.SimilarCase,
                    queryText: queryText,
                    scopes: scopes,
                    minimumAuthority: minimumAuthority,
                    topK: 3);

                var searchResp = await _mcp.SearchKnowledgeAsync(searchReq, cancellationToken).ConfigureAwait(false);
                fallbackSearches.Add(searchResp);

                foreach (var candidate in searchResp.Candidates.Take(3))
                {
                    var itemReq = _mapper.MapGetKnowledgeItemRequest(Ids.NewRequestId(intent.TaskId) + ":hydrate-similar", candidate.KnowledgeItemId);
                    var item = await _mcp.GetKnowledgeItemAsync(itemReq, cancellationToken).ConfigureAwait(false);
                    hydratedItems.Add(item);
                }

                diagnostics.Add("Fallback similar-case search executed.");
            }
        }

        // Rule B: missing constraints
        if (intent.HardConstraints.Count > 0 && merged.Constraints.Count == 0)
        {
            usedAny = true;

            foreach (var hc in intent.HardConstraints.Where(h => !string.IsNullOrWhiteSpace(h)))
            {
                var queryText = hc.Trim();
                var searchReq = _mapper.MapFallbackSearchRequest(
                    requestId: Ids.NewRequestId(intent.TaskId) + ":fallback-constraint",
                    taskId: intent.TaskId,
                    kind: QueryKind.Constraint,
                    queryText: queryText,
                    scopes: scopes,
                    minimumAuthority: minimumAuthority,
                    topK: 2);

                var searchResp = await _mcp.SearchKnowledgeAsync(searchReq, cancellationToken).ConfigureAwait(false);
                fallbackSearches.Add(searchResp);

                foreach (var candidate in searchResp.Candidates.Take(2))
                {
                    var itemReq = _mapper.MapGetKnowledgeItemRequest(Ids.NewRequestId(intent.TaskId) + ":hydrate-constraint", candidate.KnowledgeItemId);
                    var item = await _mcp.GetKnowledgeItemAsync(itemReq, cancellationToken).ConfigureAwait(false);
                    hydratedItems.Add(item);
                }
            }

            diagnostics.Add("Fallback constraint searches executed.");
        }

        // Rule C: missing anti-patterns for risk-heavy tasks
        if (intent.RiskSignals.Count > 0 && merged.AntiPatterns.Count == 0)
        {
            usedAny = true;
            var queryText = string.Join(" | ", intent.RiskSignals.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()));

            var searchReq = _mapper.MapFallbackSearchRequest(
                requestId: Ids.NewRequestId(intent.TaskId) + ":fallback-risk",
                taskId: intent.TaskId,
                kind: QueryKind.Risk,
                queryText: queryText,
                scopes: scopes,
                minimumAuthority: minimumAuthority,
                topK: 3);

            var searchResp = await _mcp.SearchKnowledgeAsync(searchReq, cancellationToken).ConfigureAwait(false);
            fallbackSearches.Add(searchResp);

            foreach (var candidate in searchResp.Candidates.Take(3))
            {
                var itemReq = _mapper.MapGetKnowledgeItemRequest(Ids.NewRequestId(intent.TaskId) + ":hydrate-risk", candidate.KnowledgeItemId);
                var item = await _mcp.GetKnowledgeItemAsync(itemReq, cancellationToken).ConfigureAwait(false);
                hydratedItems.Add(item);
            }

            diagnostics.Add("Fallback risk search executed.");
        }

        return new FallbackRetrievalResult(
            FallbackSearches: fallbackSearches,
            HydratedFallbackItems: hydratedItems,
            Diagnostics: diagnostics,
            UsedFallbackSearches: usedAny);
    }
}

public sealed record FallbackRetrievalResult(
    IReadOnlyList<SearchKnowledgeResponse> FallbackSearches,
    IReadOnlyList<GetKnowledgeItemResponse> HydratedFallbackItems,
    IReadOnlyList<string> Diagnostics,
    bool UsedFallbackSearches);

