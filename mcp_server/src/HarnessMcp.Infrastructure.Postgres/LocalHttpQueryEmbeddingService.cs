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

    public LocalHttpQueryEmbeddingService(EmbeddingConfig config, HttpClient? httpClient = null)
    {
        _config = config;
        _http = httpClient ?? new HttpClient();
        _timeout = TimeSpan.FromSeconds(Math.Max(1, config.TimeoutSeconds));
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
        var purpose = request.RequestId.Contains(':', StringComparison.Ordinal) || IsHarnessChunkQueryKind(request.QueryKind)
            ? "harness_retrieval"
            : "direct_search";

        var itemId = request.RequestId;
        var (taskId, chunkId) = TryExtractTaskAndChunkFromRequestId(request.RequestId);

        object? structuredScopes = TryMapStructuredScopes(request.Scopes);
        var chunkType = MapChunkType(request.QueryKind);
        var queryKind = request.QueryKind.ToString();
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
                    RetrievalRoleHint: queryKind,
                    Text: request.QueryText,
                    StructuredScopes: structuredScopes,
                    TaskShape: null)
            });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        using var resp = await _http.PostAsJsonAsync(
            _config.Endpoint,
            envelope,
            timeoutCts.Token).ConfigureAwait(false);

        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<BuilderApiEmbedQueryResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Embedding response missing/invalid JSON.");

        if (response.Items.Count != 1)
            throw new InvalidOperationException($"Embedding response item count invalid. expected=1 actual={response.Items.Count}");

        var item = response.Items[0];
        if (!string.Equals(item.ItemId, request.RequestId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Embedding response item_id mismatch. expected={request.RequestId} actual={item.ItemId}");

        var vector = item.Vector.ToArray();
        if (vector.Length != response.Dimension)
            throw new InvalidOperationException($"Embedding response dimension mismatch. dimension={response.Dimension} vectorLength={vector.Length}");

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

    private static (string? taskId, string? chunkId) TryExtractTaskAndChunkFromRequestId(string requestId)
    {
        // Chunk planner uses: $"{parentRequestId}:{chunkIdSuffix}"
        // Use a cheap heuristic to surface task_id and chunk_id for diagnostics.
        var parts = requestId.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return (taskId: null, chunkId: null);

        return (taskId: parts[0], chunkId: parts[^1]);
    }

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

    private static object? TryMapStructuredScopes(ScopeFilterDto scopes)
    {
        // Contract expects a generic JSON object; use a small dictionary.
        var dict = new Dictionary<string, IReadOnlyList<string>>();

        void Add(string key, IReadOnlyList<string> vals)
        {
            if (vals.Count > 0)
                dict[key] = vals;
        }

        Add(nameof(scopes.Domains), scopes.Domains);
        Add(nameof(scopes.Modules), scopes.Modules);
        Add(nameof(scopes.Features), scopes.Features);
        Add(nameof(scopes.Layers), scopes.Layers);
        Add(nameof(scopes.Concerns), scopes.Concerns);
        Add(nameof(scopes.Repos), scopes.Repos);
        Add(nameof(scopes.Services), scopes.Services);
        Add(nameof(scopes.Symbols), scopes.Symbols);

        return dict.Count == 0 ? null : dict;
    }
}

