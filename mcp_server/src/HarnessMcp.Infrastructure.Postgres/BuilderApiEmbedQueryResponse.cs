using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HarnessMcp.Infrastructure.Postgres;

internal sealed record BuilderApiEmbedQueryResponse(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("task_id")] string? TaskId,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("model_name")] string ModelName,
    [property: JsonPropertyName("model_version")] string? ModelVersion,
    [property: JsonPropertyName("normalize_embeddings")] bool NormalizeEmbeddings,
    [property: JsonPropertyName("dimension")] int Dimension,
    [property: JsonPropertyName("fallback_mode")] bool FallbackMode,
    [property: JsonPropertyName("text_processing_id")] string TextProcessingId,
    [property: JsonPropertyName("vector_space_id")] string VectorSpaceId,
    [property: JsonPropertyName("items")] IReadOnlyList<BuilderApiEmbedQueryResponseItem> Items,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

