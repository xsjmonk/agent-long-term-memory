using System.Text.Json.Serialization;

namespace HarnessMcp.Infrastructure.Postgres;

internal sealed record BuilderApiEmbedQueryRequestItem(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("chunk_id")] string? ChunkId,
    [property: JsonPropertyName("chunk_type")] string? ChunkType,
    [property: JsonPropertyName("query_kind")] string QueryKind,
    [property: JsonPropertyName("retrieval_role_hint")] string? RetrievalRoleHint,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("structured_scopes")] object? StructuredScopes,
    [property: JsonPropertyName("task_shape")] object? TaskShape);

