namespace HarnessMcp.ControlPlane;

public enum HarnessStage
{
    NeedRequirementIntent,
    NeedRetrievalChunkSet,
    NeedRetrievalChunkValidation,
    NeedMcpRetrieveMemoryByChunks,
    NeedMcpMergeRetrievalResults,
    NeedMcpBuildMemoryContextPack,
    NeedExecutionPlan,
    NeedWorkerExecutionPacket,
    Complete,
    Error
}