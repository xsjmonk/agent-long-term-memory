using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HarnessMcp.Infrastructure.Postgres;

internal sealed record BuilderApiEmbedQueryResponseItem(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("chunk_id")] string? ChunkId,
    [property: JsonPropertyName("chunk_type")] string? ChunkType,
    [property: JsonPropertyName("query_kind")] string QueryKind,
    [property: JsonPropertyName("retrieval_role_hint")] string? RetrievalRoleHint,
    [property: JsonPropertyName("vector")] IReadOnlyList<float> Vector,
    [property: JsonPropertyName("input_char_count")] int InputCharCount,
    [property: JsonPropertyName("effective_text_char_count")] int EffectiveTextCharCount,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

