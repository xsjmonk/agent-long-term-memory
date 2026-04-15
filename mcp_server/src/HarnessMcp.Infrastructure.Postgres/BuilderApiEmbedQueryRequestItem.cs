using System.Text.Json.Serialization;
using HarnessMcp.Contracts;

namespace HarnessMcp.Infrastructure.Postgres;

internal sealed record BuilderApiEmbedQueryRequestItem(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("chunk_id")] string? ChunkId,
    [property: JsonPropertyName("chunk_type")] string? ChunkType,
    [property: JsonPropertyName("query_kind")] string QueryKind,
    [property: JsonPropertyName("retrieval_role_hint")] string? RetrievalRoleHint,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("structured_scopes")] BuilderApiStructuredScopesDto? StructuredScopes,
    [property: JsonPropertyName("task_shape")] BuilderApiTaskShapeDto? TaskShape);

