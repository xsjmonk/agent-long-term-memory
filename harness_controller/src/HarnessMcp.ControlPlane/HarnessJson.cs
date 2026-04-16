using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarnessMcp.ControlPlane;

public static class HarnessJson
{
    public static RuntimeOptions DeserializeRuntimeOptions(string json) =>
        JsonSerializer.Deserialize(json, HarnessJsonContext.Default.RuntimeOptions)
        ?? throw new JsonException("RuntimeOptions deserialization returned null.");

    public static Session? DeserializeSession(string json) =>
        JsonSerializer.Deserialize(json, HarnessJsonContext.Default.Session);

    public static string SerializeSession(Session session) =>
        JsonSerializer.Serialize(session, HarnessJsonContext.Default.Session);

    public static string SerializeStepResponse(StepResponse response) =>
        JsonSerializer.Serialize(response, HarnessJsonContext.Default.StepResponse);

    public static string SerializeProtocolDescription(HarnessProtocolDescription description) =>
        JsonSerializer.Serialize(description, HarnessJsonContext.Default.HarnessProtocolDescription);

    public static JsonElement ParseJsonElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static JsonElement DeepClone(JsonElement element) => element.Clone();

    public static JsonElement CreateObject(Action<Utf8JsonWriter> write)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }
}

