using HarnessMcp.AgentClient.Support;
using HarnessMcp.AgentClient.Transport;
using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Planning;

public sealed class MemoryEvidenceHydrator
{
    private readonly IMcpToolClient _mcp;
    private readonly McpRequestMapper _mapper;

    public MemoryEvidenceHydrator(IMcpToolClient mcp, McpRequestMapper mapper)
    {
        _mcp = mcp;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<GetKnowledgeItemResponse>> HydrateFinalSelectedItemsAsync(
        BuildMemoryContextPackResponse contextPack,
        string taskId,
        IReadOnlyList<GetKnowledgeItemResponse> alreadyHydrated,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var byId = new Dictionary<Guid, GetKnowledgeItemResponse>();
        foreach (var i in alreadyHydrated)
            byId[i.Item.KnowledgeItemId] = i;

        static IEnumerable<Guid> Top3Ids(IEnumerable<MergedKnowledgeItemDto> items) =>
            items.Take(3).Select(x => x.Item.KnowledgeItemId);

        async Task HydrateMany(IEnumerable<Guid> ids, string suffix)
        {
            foreach (var id in ids)
            {
                if (byId.ContainsKey(id))
                    continue;

                var req = _mapper.MapGetKnowledgeItemRequest(Ids.NewRequestId(taskId) + ":" + suffix, id);
                var resp = await _mcp.GetKnowledgeItemAsync(req, cancellationToken).ConfigureAwait(false);
                byId[id] = resp;
            }
        }

        await HydrateMany(Top3Ids(contextPack.ContextPack.Decisions), "decisions").ConfigureAwait(false);
        await HydrateMany(Top3Ids(contextPack.ContextPack.Constraints), "constraints").ConfigureAwait(false);
        await HydrateMany(Top3Ids(contextPack.ContextPack.BestPractices), "best").ConfigureAwait(false);
        await HydrateMany(Top3Ids(contextPack.ContextPack.AntiPatterns), "anti").ConfigureAwait(false);
        await HydrateMany(Top3Ids(contextPack.ContextPack.SimilarCases), "similar").ConfigureAwait(false);

        return byId.Values.OrderBy(v => v.Item.KnowledgeItemId).ToArray();
    }
}

