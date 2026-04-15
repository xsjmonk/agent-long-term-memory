using System.Text.Json.Serialization;

namespace HarnessMcp.Host.Aot;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(string))]
public partial class SignalRJsonSerializerContext : JsonSerializerContext;

