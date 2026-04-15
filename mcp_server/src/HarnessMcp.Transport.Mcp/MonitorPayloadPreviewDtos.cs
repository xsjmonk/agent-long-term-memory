using System.Text.Json.Serialization;

namespace HarnessMcp.Transport.Mcp;

public sealed class RetrieveMemoryByChunksSummaryDto
{
    [JsonPropertyName("chunkCount")]
    public int ChunkCount { get; init; }

    [JsonPropertyName("totalItems")]
    public int TotalItems { get; init; }

    [JsonPropertyName("notes")]
    public int Notes { get; init; }

    [JsonPropertyName("elapsedMs")]
    public long ElapsedMs { get; init; }
}

public sealed class MergeRetrievalResultsSummaryDto
{
    [JsonPropertyName("decisions")]
    public int Decisions { get; init; }

    [JsonPropertyName("constraints")]
    public int Constraints { get; init; }

    [JsonPropertyName("best")]
    public int Best { get; init; }

    [JsonPropertyName("anti")]
    public int Anti { get; init; }

    [JsonPropertyName("similar")]
    public int Similar { get; init; }

    [JsonPropertyName("refs")]
    public int Refs { get; init; }

    [JsonPropertyName("structures")]
    public int Structures { get; init; }

    [JsonPropertyName("warnings")]
    public int Warnings { get; init; }

    [JsonPropertyName("elapsedMs")]
    public long ElapsedMs { get; init; }
}

public sealed class BuildMemoryContextPackSummaryDto
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; init; }

    [JsonPropertyName("decisions")]
    public int Decisions { get; init; }

    [JsonPropertyName("constraints")]
    public int Constraints { get; init; }

    [JsonPropertyName("best")]
    public int Best { get; init; }

    [JsonPropertyName("anti")]
    public int Anti { get; init; }

    [JsonPropertyName("similar")]
    public int Similar { get; init; }

    [JsonPropertyName("refs")]
    public int Refs { get; init; }

    [JsonPropertyName("structures")]
    public int Structures { get; init; }

    [JsonPropertyName("warnings")]
    public int Warnings { get; init; }

    [JsonPropertyName("elapsedMs")]
    public long ElapsedMs { get; init; }
}

public sealed class SearchKnowledgeSummaryDto
{
    [JsonPropertyName("queryKind")]
    public string QueryKind { get; init; } = string.Empty;

    [JsonPropertyName("final")]
    public int Final { get; init; }

    [JsonPropertyName("lexical")]
    public int Lexical { get; init; }

    [JsonPropertyName("vector")]
    public int Vector { get; init; }

    [JsonPropertyName("elapsedMs")]
    public long ElapsedMs { get; init; }
}

public sealed class GetKnowledgeItemSummaryDto
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("relations")]
    public int Relations { get; init; }

    [JsonPropertyName("segments")]
    public int Segments { get; init; }
}

public sealed class GetRelatedKnowledgeSummaryDto
{
    [JsonPropertyName("root")]
    public string Root { get; init; } = string.Empty;

    [JsonPropertyName("relationTypes")]
    public int RelationTypes { get; init; }

    [JsonPropertyName("returned")]
    public int Returned { get; init; }
}

public sealed class ServerInfoSummaryDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = string.Empty;
}

public sealed class ReadyResponseDto
{
    [JsonPropertyName("ready")]
    public bool Ready { get; init; } = true;
}