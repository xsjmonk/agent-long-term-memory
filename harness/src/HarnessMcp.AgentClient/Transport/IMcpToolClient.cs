using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Transport;

public interface IMcpToolClient
{
    Task<ServerInfoResponse> GetServerInfoAsync(CancellationToken cancellationToken);
    Task<RetrieveMemoryByChunksResponse> RetrieveMemoryByChunksAsync(
        RetrieveMemoryByChunksRequest request,
        CancellationToken cancellationToken);
    Task<MergeRetrievalResultsResponse> MergeRetrievalResultsAsync(
        MergeRetrievalResultsRequest request,
        CancellationToken cancellationToken);
    Task<BuildMemoryContextPackResponse> BuildMemoryContextPackAsync(
        BuildMemoryContextPackRequest request,
        CancellationToken cancellationToken);
    Task<SearchKnowledgeResponse> SearchKnowledgeAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken);
    Task<GetKnowledgeItemResponse> GetKnowledgeItemAsync(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken);
    Task<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken);
}

