using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.AgentClient.Support;

namespace HarnessMcp.AgentClient.Transport;

public sealed class HttpMcpToolClient : IMcpToolClient
{
    private const string ProtocolVersion = "2025-11-25";

    private readonly HttpClient _http;
    private readonly string[] _toolBaseUrls;
    private readonly object _preflightGate = new();
    private Task<ServerInfoResponse>? _preflightTask;
    private ServerInfoResponse? _serverInfo;

    public HttpMcpToolClient(string mcpBaseUrl, HttpClient? httpClient = null)
    {
        var trimmed = mcpBaseUrl.TrimEnd('/');
        _toolBaseUrls = BuildCandidateUrls(trimmed);
        _http = httpClient ?? new HttpClient();
    }

    public async Task<ServerInfoResponse> GetServerInfoAsync(CancellationToken cancellationToken)
    {
        lock (_preflightGate)
        {
            _preflightTask ??= CallToolAsync<ServerInfoResponse>("get_server_info", null, cancellationToken);
        }

        var info = await _preflightTask.ConfigureAwait(false);
        lock (_preflightGate)
        {
            _serverInfo = info;
        }
        return info;
    }

    public async Task<RetrieveMemoryByChunksResponse> RetrieveMemoryByChunksAsync(
        RetrieveMemoryByChunksRequest request,
        CancellationToken cancellationToken)
    {
        await EnsurePreflightOkAsync(cancellationToken).ConfigureAwait(false);
        return await CallToolAsync<RetrieveMemoryByChunksResponse>(
            "retrieve_memory_by_chunks",
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MergeRetrievalResultsResponse> MergeRetrievalResultsAsync(
        MergeRetrievalResultsRequest request,
        CancellationToken cancellationToken)
    {
        await EnsurePreflightOkAsync(cancellationToken).ConfigureAwait(false);
        return await CallToolAsync<MergeRetrievalResultsResponse>(
            "merge_retrieval_results",
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<BuildMemoryContextPackResponse> BuildMemoryContextPackAsync(
        BuildMemoryContextPackRequest request,
        CancellationToken cancellationToken)
    {
        await EnsurePreflightOkAsync(cancellationToken).ConfigureAwait(false);
        return await CallToolAsync<BuildMemoryContextPackResponse>(
            "build_memory_context_pack",
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<SearchKnowledgeResponse> SearchKnowledgeAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken)
    {
        await EnsurePreflightOkAsync(cancellationToken).ConfigureAwait(false);
        return await CallToolAsync<SearchKnowledgeResponse>(
            "search_knowledge",
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GetKnowledgeItemResponse> GetKnowledgeItemAsync(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken)
    {
        await EnsurePreflightOkAsync(cancellationToken).ConfigureAwait(false);
        return await CallToolAsync<GetKnowledgeItemResponse>(
            "get_knowledge_item",
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken)
    {
        await EnsurePreflightOkAsync(cancellationToken).ConfigureAwait(false);
        return await CallToolAsync<GetRelatedKnowledgeResponse>(
            "get_related_knowledge",
            request,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsurePreflightOkAsync(CancellationToken cancellationToken)
    {
        var info = await GetServerInfoAsync(cancellationToken).ConfigureAwait(false);
        var missing = new List<string>();

        if (!info.Features.RetrieveMemoryByChunks) missing.Add("RetrieveMemoryByChunks");
        if (!info.Features.MergeRetrievalResults) missing.Add("MergeRetrievalResults");
        if (!info.Features.BuildMemoryContextPack) missing.Add("BuildMemoryContextPack");
        if (!info.Features.SearchKnowledge) missing.Add("SearchKnowledge");
        if (!info.Features.GetKnowledgeItem) missing.Add("GetKnowledgeItem");
        if (!info.Features.GetRelatedKnowledge) missing.Add("GetRelatedKnowledge");

        // Also sanity-check schema-set names (must be non-empty).
        if (string.IsNullOrWhiteSpace(info.SchemaSet.RetrieveMemoryByChunks)) missing.Add("SchemaSet.RetrieveMemoryByChunks");
        if (string.IsNullOrWhiteSpace(info.SchemaSet.MergeRetrievalResults)) missing.Add("SchemaSet.MergeRetrievalResults");
        if (string.IsNullOrWhiteSpace(info.SchemaSet.BuildMemoryContextPack)) missing.Add("SchemaSet.BuildMemoryContextPack");
        if (string.IsNullOrWhiteSpace(info.SchemaSet.SearchKnowledge)) missing.Add("SchemaSet.SearchKnowledge");
        if (string.IsNullOrWhiteSpace(info.SchemaSet.GetKnowledgeItem)) missing.Add("SchemaSet.GetKnowledgeItem");
        if (string.IsNullOrWhiteSpace(info.SchemaSet.GetRelatedKnowledge)) missing.Add("SchemaSet.GetRelatedKnowledge");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException("MCP server missing required tools: " + string.Join(", ", missing));
        }
    }

    private async Task<T> CallToolAsync<T>(string toolName, object? arguments, CancellationToken cancellationToken)
    {
        var endpointCandidates = _toolBaseUrls;
        var requestId = Guid.NewGuid().ToString("N");

        foreach (var endpoint in endpointCandidates)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));
            req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("text/event-stream"));
            req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);

            var rpc = new
            {
                jsonrpc = "2.0",
                id = requestId,
                method = "tools/call",
                @params = arguments is null
                    ? (object)new { name = toolName }
                    : new { name = toolName, arguments = arguments }
            };

            // ASP.NET stream handler expects both application/json and text/event-stream in Accept.
            req.Content = new StringContent(JsonSerializer.Serialize(rpc, JsonHelpers.Default), Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound && endpoint != endpointCandidates[^1])
                continue;

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"MCP tool call failed: tool={toolName}, url={endpoint}, status={(int)resp.StatusCode}. Body: {body}");

            return ParseToolResult<T>(body);
        }

        throw new InvalidOperationException("All MCP endpoints failed for tool call: " + toolName);
    }

    private static T ParseToolResult<T>(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";
            throw new InvalidOperationException("MCP JSON-RPC error: " + msg);
        }

        if (!root.TryGetProperty("result", out var result))
            throw new InvalidOperationException("MCP JSON-RPC response missing result.");

        // If the result is a DTO directly, deserialize it.
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("schemaVersion", out _))
        {
            return JsonSerializer.Deserialize<T>(result.GetRawText(), JsonHelpers.Default)
                   ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from MCP result.");
        }

        // Otherwise extract the first content item.
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0)
        {
            var item0 = content[0];
            if (item0.ValueKind == JsonValueKind.Object && item0.TryGetProperty("text", out var textElem) && textElem.ValueKind == JsonValueKind.String)
            {
                var text = textElem.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException("MCP tool result content.text was empty.");

                // Server tool implementations return JSON DTOs; MCP wraps them as JSON strings in content.text.
                using var inner = JsonDocument.Parse(text);
                return JsonSerializer.Deserialize<T>(inner.RootElement.GetRawText(), JsonHelpers.Default)
                       ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from MCP content.text.");
            }

            if (item0.ValueKind == JsonValueKind.Object && item0.TryGetProperty("json", out var jsonElem))
            {
                return JsonSerializer.Deserialize<T>(jsonElem.GetRawText(), JsonHelpers.Default)
                       ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from MCP content.json.");
            }
        }

        // Last resort: try deserializing the whole result wrapper.
        var fallback = JsonSerializer.Deserialize<T>(result.GetRawText(), JsonHelpers.Default);
        if (fallback is null)
            throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from MCP response.");
        return fallback;
    }

    private static string[] BuildCandidateUrls(string baseUrl)
    {
        if (baseUrl.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
            return new[] { baseUrl };

        return new[] { baseUrl, baseUrl + "/mcp" };
    }
}

