namespace HarnessMcp.ControlPlane;

public static class HarnessActionName
{
    public const string AgentGenerateRequirementIntent = "agent_generate_requirement_intent";
    public const string AgentGenerateRetrievalChunkSet = "agent_generate_retrieval_chunk_set";
    public const string AgentValidateChunkQuality = "agent_validate_chunk_quality";
    public const string AgentCallMcpRetrieveMemoryByChunks = "agent_call_mcp_retrieve_memory_by_chunks";
    public const string AgentCallMcpMergeRetrievalResults = "agent_call_mcp_merge_retrieval_results";
    public const string AgentCallMcpBuildMemoryContextPack = "agent_call_mcp_build_memory_context_pack";
    public const string AgentGenerateExecutionPlan = "agent_generate_execution_plan";
    public const string AgentGenerateWorkerExecutionPacket = "agent_generate_worker_execution_packet";
    public const string Complete = "complete";
    public const string StopWithError = "stop_with_error";
}