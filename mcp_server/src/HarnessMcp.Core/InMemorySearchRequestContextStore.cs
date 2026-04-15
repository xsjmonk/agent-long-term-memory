using System.Collections.Concurrent;

namespace HarnessMcp.Core;

/// <summary>
/// MCP-owned internal request context store.
/// Used to correlate a search request with /embed-query envelope fields and SimilarCase structural ranking.
/// This is NOT a harness dependency - it is MCP internal state created from MCP request DTOs
/// and lives only during request processing (not persisted, not exposed publicly).
/// </summary>
public sealed class InMemorySearchRequestContextStore : ISearchRequestContextStore
{
    private readonly ConcurrentDictionary<string, SearchRequestContext> _store = new();

    public void Set(string requestId, SearchRequestContext context)
    {
        _store[requestId] = context;
    }

    public bool TryGet(string requestId, out SearchRequestContext? context)
    {
        return _store.TryGetValue(requestId, out context);
    }

    public void Remove(string requestId)
    {
        _store.TryRemove(requestId, out _);
    }
}