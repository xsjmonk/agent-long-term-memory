using System.Collections.Concurrent;
using HarnessMcp.Contracts;
using HarnessMcp.Core;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class InMemoryContextPackCache : IContextPackCache
{
    private readonly ConcurrentDictionary<string, BuildMemoryContextPackResponse> _map = new();

    public void Put(BuildMemoryContextPackResponse response) => _map[response.TaskId] = response;

    public bool TryGet(string taskId, out BuildMemoryContextPackResponse? response) =>
        _map.TryGetValue(taskId, out response);
}
