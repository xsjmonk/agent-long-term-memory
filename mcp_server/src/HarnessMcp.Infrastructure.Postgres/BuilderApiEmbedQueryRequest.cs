using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HarnessMcp.Infrastructure.Postgres;

internal sealed record BuilderApiEmbedQueryRequest(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("task_id")] string? TaskId,
    [property: JsonPropertyName("caller")] string Caller,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("items")] IReadOnlyList<BuilderApiEmbedQueryRequestItem> Items);

