using System.Text.Json.Serialization;
using System.Text.Json;

namespace HarnessMcp.ControlPlane;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RuntimeOptions))]
[JsonSerializable(typeof(ValidationOptions))]
[JsonSerializable(typeof(Session))]
[JsonSerializable(typeof(StepResponse))]
[JsonSerializable(typeof(HarnessProtocolDescription))]
[JsonSerializable(typeof(StartSessionRequest))]
[JsonSerializable(typeof(SubmitStepResultRequest))]
[JsonSerializable(typeof(Artifact))]
[JsonSerializable(typeof(InputContract))]
[JsonSerializable(typeof(CompletionArtifacts))]
[JsonSerializable(typeof(CommandDescription))]
[JsonSerializable(typeof(StageDescription))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonDocument))]
public partial class HarnessJsonContext : JsonSerializerContext { }

