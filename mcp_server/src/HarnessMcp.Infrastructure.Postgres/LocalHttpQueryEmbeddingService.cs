using System.Net.Http.Json;
using System.Collections.Generic;
using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.Core;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class LocalHttpQueryEmbeddingService : IQueryEmbeddingService
{
    private readonly EmbeddingConfig _config;
    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly ISearchRequestContextStore? _contextStore;

    public LocalHttpQueryEmbeddingService(EmbeddingConfig config, HttpClient? httpClient = null, ISearchRequestContextStore? contextStore = null)
    {
        _config = config;
        _http = httpClient ?? new HttpClient();
        _timeout = TimeSpan.FromSeconds(Math.Max(1, config.TimeoutSeconds));
        _contextStore = contextStore;
    }

    public async ValueTask<QueryEmbeddingResult> EmbedAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.QueryText))
            return new QueryEmbeddingResult(
                Vector: ReadOnlyMemory<float>.Empty,
                Provider: "local-http",
                ModelName: "local-http",
                ModelVersion: null,
                NormalizeEmbeddings: false,
                Dimension: 0,
                FallbackMode: false,
                TextProcessingId: "empty",
                VectorSpaceId: "empty",
                InputCharCount: 0,
                EffectiveTextCharCount: 0,
                Truncated: false,
                Warnings: Array.Empty<string>());

        // Budget-saving: send exactly one item per request.
        // Build the richer envelope so the harness can correlate semantic degradation signals.
        var purpose = (_contextStore?.TryGet(request.RequestId, out var _) == true)
            ? "harness_retrieval"
            : "direct_search";

        var itemId = request.RequestId;

        // Get MCP-owned request context
        string? taskId = null;
        string? chunkId = null;
        string? chunkType = null;
        string? retrievalRoleHint = null;
        SimilarCaseShapeDto? taskShape = null;
        ScopeFilterDto? requestScopes = null;

        if (_contextStore?.TryGet(request.RequestId, out var context) == true && context != null)
        {
            taskId = context.TaskId;
            chunkId = context.ChunkId;
            chunkType = context.ChunkType;
            retrievalRoleHint = context.RetrievalRoleHint;
            taskShape = context.TaskShape;
            requestScopes = context.StructuredScopes;
        }
        else
        {
            // Direct search: keep nulls, but still set purpose appropriately
            chunkType = MapChunkType(request.QueryKind);
            retrievalRoleHint = request.QueryKind.ToString();
            requestScopes = request.Scopes;
        }

        BuilderApiStructuredScopesDto? structuredScopes = requestScopes != null ? TryMapStructuredScopes(requestScopes) : null;
        var queryKind = request.QueryKind.ToString();

        BuilderApiTaskShapeDto? builderTaskShape = null;
        if (taskShape != null)
        {
            builderTaskShape = new BuilderApiTaskShapeDto(
                TaskType: taskShape.TaskType,
                FeatureShape: taskShape.FeatureShape,
                EngineChangeAllowed: taskShape.EngineChangeAllowed,
                LikelyLayers: taskShape.LikelyLayers ?? Array.Empty<string>(),
                RiskSignals: taskShape.RiskSignals ?? Array.Empty<string>(),
                Complexity: taskShape.Complexity);
        }

        var envelope = new BuilderApiEmbedQueryRequest(
            SchemaVersion: "1.1",
            RequestId: request.RequestId,
            TaskId: taskId,
            Caller: "mcp",
            Purpose: purpose,
            Items: new[]
            {
                new BuilderApiEmbedQueryRequestItem(
                    ItemId: itemId,
                    ChunkId: chunkId,
                    ChunkType: chunkType,
                    QueryKind: queryKind,
                    RetrievalRoleHint: retrievalRoleHint,
                    Text: request.QueryText,
                    StructuredScopes: structuredScopes,
                    TaskShape: builderTaskShape)
            });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        using var resp = await _http.PostAsJsonAsync(
            _config.Endpoint,
            envelope,
            InfrastructureJsonSerializerContext.Default.BuilderApiEmbedQueryRequest,
            timeoutCts.Token).ConfigureAwait(false);

        resp.EnsureSuccessStatusCode();

        var response = await resp.Content.ReadFromJsonAsync(
            InfrastructureJsonSerializerContext.Default.BuilderApiEmbedQueryResponse,
            timeoutCts.Token).ConfigureAwait(false) 
            ?? throw new InvalidOperationException("Embedding response missing/invalid JSON.");

        // Envelope-level validation
        if (response.SchemaVersion != "1.1")
            throw new InvalidOperationException($"Embedding response schema_version mismatch. expected=1.1 actual={response.SchemaVersion}");

        if (response.RequestId != request.RequestId)
            throw new InvalidOperationException($"Embedding response request_id mismatch. expected={request.RequestId} actual={response.RequestId}");

        if (!string.IsNullOrEmpty(taskId) && response.TaskId != taskId)
            throw new InvalidOperationException($"Embedding response task_id mismatch. expected={taskId} actual={response.TaskId}");

        if (response.Items.Count != 1)
            throw new InvalidOperationException($"Embedding response item count invalid. expected=1 actual={response.Items.Count}");

        if (response.Dimension <= 0)
            throw new InvalidOperationException($"Embedding response dimension invalid. expected>0 actual={response.Dimension}");

        var item = response.Items[0];

        // Item-level validation
        if (item.ItemId != request.RequestId)
            throw new InvalidOperationException($"Embedding response item_id mismatch. expected={request.RequestId} actual={item.ItemId}");

        if (!string.IsNullOrEmpty(chunkId) && item.ChunkId != chunkId)
            throw new InvalidOperationException($"Embedding response chunk_id mismatch. expected={chunkId} actual={item.ChunkId}");

        if (!string.IsNullOrEmpty(chunkType) && item.ChunkType != chunkType)
            throw new InvalidOperationException($"Embedding response chunk_type mismatch. expected={chunkType} actual={item.ChunkType}");

        if (item.QueryKind != queryKind)
            throw new InvalidOperationException($"Embedding response query_kind mismatch. expected={queryKind} actual={item.QueryKind}");

        if (!string.IsNullOrEmpty(retrievalRoleHint) && item.RetrievalRoleHint != retrievalRoleHint)
            throw new InvalidOperationException($"Embedding response retrieval_role_hint mismatch. expected={retrievalRoleHint} actual={item.RetrievalRoleHint}");

        if (item.Vector.Count != response.Dimension)
            throw new InvalidOperationException($"Embedding response vector length mismatch. dimension={response.Dimension} vectorLength={item.Vector.Count}");

        if (item.EffectiveTextCharCount > item.InputCharCount)
            throw new InvalidOperationException($"Embedding response effective_text_char_count > input_char_count. input={item.InputCharCount} effective={item.EffectiveTextCharCount}");

        if (item.Truncated && item.EffectiveTextCharCount == item.InputCharCount)
            throw new InvalidOperationException($"Embedding response truncated=true but effective_text_char_count == input_char_count.");

        var vector = item.Vector.ToArray();

        var warnings = new List<string>(response.Warnings.Count + item.Warnings.Count);
        warnings.AddRange(response.Warnings);
        warnings.AddRange(item.Warnings);

        return new QueryEmbeddingResult(
            Vector: vector,
            Provider: response.Provider,
            ModelName: response.ModelName,
            ModelVersion: response.ModelVersion,
            NormalizeEmbeddings: response.NormalizeEmbeddings,
            Dimension: response.Dimension,
            FallbackMode: response.FallbackMode,
            TextProcessingId: response.TextProcessingId,
            VectorSpaceId: response.VectorSpaceId,
            InputCharCount: item.InputCharCount,
            EffectiveTextCharCount: item.EffectiveTextCharCount,
            Truncated: item.Truncated,
            Warnings: warnings);
    }

    private static bool IsHarnessChunkQueryKind(QueryKind kind) =>
        kind is QueryKind.CoreTask or QueryKind.Constraint or QueryKind.Risk or QueryKind.Pattern or QueryKind.SimilarCase;

    private static string? MapChunkType(QueryKind kind) =>
        kind switch
        {
            QueryKind.CoreTask => "core_task",
            QueryKind.Constraint => "constraint",
            QueryKind.Risk => "risk",
            QueryKind.Pattern => "pattern",
            QueryKind.SimilarCase => "similar_case",
            _ => null
        };

    private static BuilderApiStructuredScopesDto? TryMapStructuredScopes(ScopeFilterDto scopes)
    {
        var domains = scopes.Domains.Count > 0 ? scopes.Domains : null;
        var modules = scopes.Modules.Count > 0 ? scopes.Modules : null;
        var features = scopes.Features.Count > 0 ? scopes.Features : null;
        var layers = scopes.Layers.Count > 0 ? scopes.Layers : null;
        var concerns = scopes.Concerns.Count > 0 ? scopes.Concerns : null;
        var repos = scopes.Repos.Count > 0 ? scopes.Repos : null;
        var services = scopes.Services.Count > 0 ? scopes.Services : null;
        var symbols = scopes.Symbols.Count > 0 ? scopes.Symbols : null;

        // Return null if no scopes were populated
        if (domains is null && modules is null && features is null &&
            layers is null && concerns is null && repos is null &&
            services is null && symbols is null)
            return null;

        return new BuilderApiStructuredScopesDto
        {
            Domains = domains,
            Modules = modules,
            Features = features,
            Layers = layers,
            Concerns = concerns,
            Repos = repos,
            Services = services,
            Symbols = symbols
        };
    }
}

