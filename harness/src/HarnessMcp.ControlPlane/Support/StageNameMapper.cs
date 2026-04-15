namespace HarnessMcp.ControlPlane.Support;

public static class StageNameMapper
{
    public static string ToProtocolName(HarnessStage stage)
    {
        return stage switch
        {
            HarnessStage.NeedRequirementIntent => "need_requirement_intent",
            HarnessStage.NeedRetrievalChunkSet => "need_retrieval_chunk_set",
            HarnessStage.NeedRetrievalChunkValidation => "need_retrieval_chunk_validation",
            HarnessStage.NeedMcpRetrieveMemoryByChunks => "need_mcp_retrieve_memory_by_chunks",
            HarnessStage.NeedMcpMergeRetrievalResults => "need_mcp_merge_retrieval_results",
            HarnessStage.NeedMcpBuildMemoryContextPack => "need_mcp_build_memory_context_pack",
            HarnessStage.NeedExecutionPlan => "need_execution_plan",
            HarnessStage.NeedWorkerExecutionPacket => "need_worker_execution_packet",
            HarnessStage.Complete => "complete",
            HarnessStage.Error => "error",
            _ => stage.ToString().ToLowerInvariant()
        };
    }

    public static HarnessStage? FromProtocolName(string protocolName)
    {
        return protocolName.ToLowerInvariant() switch
        {
            "need_requirement_intent" => HarnessStage.NeedRequirementIntent,
            "need_retrieval_chunk_set" => HarnessStage.NeedRetrievalChunkSet,
            "need_retrieval_chunk_validation" => HarnessStage.NeedRetrievalChunkValidation,
            "need_mcp_retrieve_memory_by_chunks" => HarnessStage.NeedMcpRetrieveMemoryByChunks,
            "need_mcp_merge_retrieval_results" => HarnessStage.NeedMcpMergeRetrievalResults,
            "need_mcp_build_memory_context_pack" => HarnessStage.NeedMcpBuildMemoryContextPack,
            "need_execution_plan" => HarnessStage.NeedExecutionPlan,
            "need_worker_execution_packet" => HarnessStage.NeedWorkerExecutionPacket,
            "complete" => HarnessStage.Complete,
            "error" => HarnessStage.Error,
            _ => null
        };
    }
}