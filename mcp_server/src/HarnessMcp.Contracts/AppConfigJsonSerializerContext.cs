using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarnessMcp.Contracts;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true)]
[
    JsonSerializable(typeof(AppConfig)),
    JsonSerializable(typeof(ServerConfig)),
    JsonSerializable(typeof(DatabaseConfig)),
    JsonSerializable(typeof(RetrievalConfig)),
    JsonSerializable(typeof(EmbeddingConfig)),
    JsonSerializable(typeof(LoggingConfig)),
    JsonSerializable(typeof(MonitoringConfig)),
    JsonSerializable(typeof(FeatureConfig)),
    JsonSerializable(typeof(TransportMode)),
    JsonSerializable(typeof(AuthorityLevel))
]
public partial class AppConfigJsonSerializerContext : JsonSerializerContext;

