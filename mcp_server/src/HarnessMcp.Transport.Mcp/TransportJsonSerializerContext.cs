using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarnessMcp.Transport.Mcp;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RetrieveMemoryByChunksSummaryDto))]
[JsonSerializable(typeof(MergeRetrievalResultsSummaryDto))]
[JsonSerializable(typeof(BuildMemoryContextPackSummaryDto))]
[JsonSerializable(typeof(SearchKnowledgeSummaryDto))]
[JsonSerializable(typeof(GetKnowledgeItemSummaryDto))]
[JsonSerializable(typeof(GetRelatedKnowledgeSummaryDto))]
[JsonSerializable(typeof(ServerInfoSummaryDto))]
[JsonSerializable(typeof(ReadyResponseDto))]
public partial class TransportJsonSerializerContext : JsonSerializerContext;