using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarnessMcp.AgentClient.Support;

public static class JsonHelpers
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
    };

    public static string Serialize<T>(T value, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Serialize(value, options ?? Default);
    }

    public static T Deserialize<T>(string json, JsonSerializerOptions? options = null)
    {
        var opts = options ?? Default;
        var v = JsonSerializer.Deserialize<T>(json, opts);
        if (v is null)
            throw new InvalidOperationException($"Failed to deserialize JSON into {typeof(T).Name}.");
        return v;
    }

    public static string CompactJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, Default);
    }

    public static bool TryGetJsonObject(string? input, out string jsonObject)
    {
        jsonObject = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(input);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            jsonObject = JsonSerializer.Serialize(doc.RootElement, Default);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ReadAllTextSafe(string path)
    {
        // Avoid BOM surprises by letting UTF8 decode with detection.
        return File.ReadAllText(path, Encoding.UTF8);
    }
}

