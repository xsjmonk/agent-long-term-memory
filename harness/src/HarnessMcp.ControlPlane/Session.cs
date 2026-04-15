using System.Text.Json.Serialization;

namespace HarnessMcp.ControlPlane;

public class Session
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyName("rawTask")]
    public string RawTask { get; set; } = string.Empty;

    [JsonPropertyName("currentStage")]
    public HarnessStage CurrentStage { get; set; }

    [JsonPropertyName("lastAcceptedAction")]
    public string? LastAcceptedAction { get; set; }

    [JsonPropertyName("acceptedRequirementIntent")]
    public object? AcceptedRequirementIntent { get; set; }

    [JsonPropertyName("acceptedRetrievalChunkSet")]
    public object? AcceptedRetrievalChunkSet { get; set; }

    [JsonPropertyName("acceptedChunkQualityReport")]
    public object? AcceptedChunkQualityReport { get; set; }

    [JsonPropertyName("acceptedRetrieveMemoryByChunksResponse")]
    public object? AcceptedRetrieveMemoryByChunksResponse { get; set; }

    [JsonPropertyName("acceptedMergeRetrievalResultsResponse")]
    public object? AcceptedMergeRetrievalResultsResponse { get; set; }

    [JsonPropertyName("acceptedBuildMemoryContextPackResponse")]
    public object? AcceptedBuildMemoryContextPackResponse { get; set; }

    [JsonPropertyName("acceptedExecutionPlan")]
    public object? AcceptedExecutionPlan { get; set; }

    [JsonPropertyName("acceptedWorkerExecutionPacket")]
    public object? AcceptedWorkerExecutionPacket { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [JsonPropertyName("updatedUtc")]
    public DateTime UpdatedUtc { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}