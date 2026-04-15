using System.Text.Json.Serialization;

namespace HarnessMcp.ControlPlane;

public class HarnessProtocolDescription
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("commands")]
    public List<CommandDescription> Commands { get; set; } = new()
    {
        new CommandDescription
        {
            Name = "start-session",
            Description = "Start a new planning session with a raw task",
            InputSchema = "StartSessionRequest",
            OutputSchema = "StepResponse"
        },
        new CommandDescription
        {
            Name = "get-next-step",
            Description = "Get the current required step for an existing session",
            InputSchema = "{ sessionId: string }",
            OutputSchema = "StepResponse"
        },
        new CommandDescription
        {
            Name = "submit-step-result",
            Description = "Submit a step result to advance the session",
            InputSchema = "SubmitStepResultRequest",
            OutputSchema = "StepResponse"
        },
        new CommandDescription
        {
            Name = "get-session-status",
            Description = "Get the current status of a session",
            InputSchema = "{ sessionId: string }",
            OutputSchema = "StepResponse"
        },
        new CommandDescription
        {
            Name = "cancel-session",
            Description = "Cancel an existing session",
            InputSchema = "{ sessionId: string }",
            OutputSchema = "StepResponse"
        },
        new CommandDescription
        {
            Name = "describe-protocol",
            Description = "Describe the harness protocol",
            InputSchema = "none",
            OutputSchema = "HarnessProtocolDescription"
        }
    };

    [JsonPropertyName("stages")]
    public List<StageDescription> Stages { get; set; } = new()
    {
        new StageDescription { Name = "need_requirement_intent", NextAction = "agent_generate_requirement_intent" },
        new StageDescription { Name = "need_retrieval_chunk_set", NextAction = "agent_generate_retrieval_chunk_set" },
        new StageDescription { Name = "need_retrieval_chunk_validation", NextAction = "agent_validate_chunk_quality" },
        new StageDescription { Name = "need_mcp_retrieve_memory_by_chunks", NextAction = "agent_call_mcp_retrieve_memory_by_chunks" },
        new StageDescription { Name = "need_mcp_merge_retrieval_results", NextAction = "agent_call_mcp_merge_retrieval_results" },
        new StageDescription { Name = "need_mcp_build_memory_context_pack", NextAction = "agent_call_mcp_build_memory_context_pack" },
        new StageDescription { Name = "need_execution_plan", NextAction = "agent_generate_execution_plan" },
        new StageDescription { Name = "need_worker_execution_packet", NextAction = "agent_generate_worker_execution_packet" },
        new StageDescription { Name = "complete", NextAction = "complete" },
        new StageDescription { Name = "error", NextAction = "stop_with_error" }
    };
}

public class CommandDescription
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public string InputSchema { get; set; } = string.Empty;

    [JsonPropertyName("outputSchema")]
    public string OutputSchema { get; set; } = string.Empty;
}

public class StageDescription
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("nextAction")]
    public string NextAction { get; set; } = string.Empty;
}