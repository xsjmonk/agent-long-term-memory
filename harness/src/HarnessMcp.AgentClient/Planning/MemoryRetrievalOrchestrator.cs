using HarnessMcp.AgentClient.Support;
using HarnessMcp.AgentClient.Transport;
using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Planning;

public sealed class MemoryRetrievalOrchestrator
{
    private readonly IMcpToolClient _mcp;
    private readonly McpRequestMapper _mapper;

    public MemoryRetrievalOrchestrator(IMcpToolClient mcp, McpRequestMapper mapper)
    {
        _mcp = mcp;
        _mapper = mapper;
    }

    public async Task<PrimaryRetrievalResult> RetrievePrimaryAsync(
        RequirementIntent intent,
        RetrievalChunkSet chunkSet,
        AgentClient.Config.AgentClientOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestIdBase = Ids.NewRequestId(intent.TaskId);

        // 8) Map to RetrieveMemoryByChunksRequest
        var retrieveReq = _mapper.MapRetrieveMemoryByChunksRequest(
            intent,
            chunkSet,
            requestId: requestIdBase + ":retrieve",
            minimumAuthority: options.MinimumAuthority,
            maxItemsPerChunk: options.MaxItemsPerChunk,
            chunkSearchDiagnostics: Array.Empty<string>(),
            cancellationToken: cancellationToken);

        // 9) call retrieve_memory_by_chunks
        var retrieved = await _mcp.RetrieveMemoryByChunksAsync(retrieveReq, cancellationToken).ConfigureAwait(false);

        // 10) call merge_retrieval_results
        var mergeReq = _mapper.MapMergeRetrievalResultsRequest(
            chunkSet,
            retrieved,
            requestId: requestIdBase + ":merge");
        var merged = await _mcp.MergeRetrievalResultsAsync(mergeReq, cancellationToken).ConfigureAwait(false);

        // 11) call build_memory_context_pack
        var buildReq = _mapper.MapBuildMemoryContextPackRequest(
            intent,
            chunkSet,
            retrieved,
            merged,
            requestId: requestIdBase + ":contextpack");
        var contextPack = await _mcp.BuildMemoryContextPackAsync(buildReq, cancellationToken).ConfigureAwait(false);

        var diagnostics = contextPack.Diagnostics.Warnings;

        return new PrimaryRetrievalResult(
            Retrieved: retrieved,
            Merged: merged,
            ContextPack: contextPack,
            Diagnostics: diagnostics);
    }
}

public sealed record PrimaryRetrievalResult(
    RetrieveMemoryByChunksResponse Retrieved,
    MergeRetrievalResultsResponse Merged,
    BuildMemoryContextPackResponse ContextPack,
    IReadOnlyList<string> Diagnostics);

