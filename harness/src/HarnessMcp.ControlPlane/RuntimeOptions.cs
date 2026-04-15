using System.Text.Json;

namespace HarnessMcp.ControlPlane;

public class RuntimeOptions
{
    private static readonly string DefaultConfigPath = Path.Combine(AppContext.BaseDirectory, "config", "appsettings.harness.json");

    public string SessionsRoot { get; set; } = ".harness/sessions";
    public string SchemaVersion { get; set; } = "1.0";
    public ValidationOptions Validation { get; set; } = new();

    private static RuntimeOptions? _cached;

    public static RuntimeOptions Load(string? configPath = null, Dictionary<string, string>? envVars = null)
    {
        if (_cached != null)
            return _cached;

        var options = new RuntimeOptions();
        var path = configPath ?? DefaultConfigPath;

        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<RuntimeOptions>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (loaded != null)
                options = loaded;
        }

        if (envVars != null)
        {
            if (envVars.TryGetValue("HARNESS_SESSIONS_ROOT", out var sessionsRoot))
                options.SessionsRoot = sessionsRoot;
            if (envVars.TryGetValue("HARNESS_SCHEMA_VERSION", out var schemaVersion))
                options.SchemaVersion = schemaVersion;
        }

        _cached = options;
        return options;
    }

    public static void ClearCache() => _cached = null;
}

public class ValidationOptions
{
    public bool RequireConstraintChunk { get; set; } = true;
    public bool RequireRiskChunk { get; set; } = true;
    public int MaxChunks { get; set; } = 10;
    public int MaxPlanSteps { get; set; } = 50;
    public int ChunkTextMaxLength { get; set; } = 240;
}