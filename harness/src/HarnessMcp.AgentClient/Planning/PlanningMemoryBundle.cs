using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Planning;

public sealed record PlanningMemoryBundle(
    RetrieveMemoryByChunksResponse Retrieved,
    MergeRetrievalResultsResponse Merged,
    BuildMemoryContextPackResponse ContextPack,
    IReadOnlyList<GetKnowledgeItemResponse> HydratedItems,
    IReadOnlyList<SearchKnowledgeResponse> FallbackSearches,
    IReadOnlyList<string> Diagnostics,
    bool UsedFallbackSearches);

