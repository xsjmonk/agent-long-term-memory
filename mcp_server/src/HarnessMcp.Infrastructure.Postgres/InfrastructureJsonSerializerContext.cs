using System.Text.Json;
using System.Text.Json.Serialization;
using HarnessMcp.Contracts;

namespace HarnessMcp.Infrastructure.Postgres;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(BuilderApiEmbedQueryRequest))]
[JsonSerializable(typeof(BuilderApiEmbedQueryRequestItem))]
[JsonSerializable(typeof(BuilderApiEmbedQueryResponse))]
[JsonSerializable(typeof(BuilderApiEmbedQueryResponseItem))]
[JsonSerializable(typeof(BuilderApiStructuredScopesDto))]
[JsonSerializable(typeof(BuilderApiTaskShapeDto))]
[JsonSerializable(typeof(SimilarCaseShapeDto))]
[JsonSerializable(typeof(string[]))]
internal partial class InfrastructureJsonSerializerContext : JsonSerializerContext;