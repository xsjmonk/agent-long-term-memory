using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HarnessMcp.Infrastructure.Postgres;

internal sealed class BuilderApiStructuredScopesDto
{
    [JsonPropertyName("domains")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Domains { get; init; }

    [JsonPropertyName("modules")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Modules { get; init; }

    [JsonPropertyName("features")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Features { get; init; }

    [JsonPropertyName("layers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Layers { get; init; }

    [JsonPropertyName("concerns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Concerns { get; init; }

    [JsonPropertyName("repos")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Repos { get; init; }

    [JsonPropertyName("services")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Services { get; init; }

    [JsonPropertyName("symbols")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Symbols { get; init; }
}