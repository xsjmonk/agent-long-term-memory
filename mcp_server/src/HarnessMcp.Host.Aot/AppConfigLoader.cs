using System.Text.Json;
using HarnessMcp.Contracts;

namespace HarnessMcp.Host.Aot;

public static class AppConfigLoader
{
    public static AppConfig Load(string[] args)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "appsettings.mcp.json");
        return LoadFromPath(path, args);
    }

    internal static AppConfig LoadFromPath(string path, string[] args)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Missing config: {fullPath}");

        var json = File.ReadAllText(fullPath);

        var cfg = JsonSerializer.Deserialize(json, AppConfigJsonSerializerContext.Default.AppConfig);
        if (cfg is null)
            throw new InvalidOperationException($"Failed to deserialize AppConfig from '{fullPath}'.");

        // Apply command-line override after file is loaded.
        if (args.Length > 0 && string.Equals(args[0], "--transport", StringComparison.OrdinalIgnoreCase) && args.Length > 1)
        {
            if (Enum.TryParse<TransportMode>(args[1], ignoreCase: true, out var mode))
                cfg.Server.TransportMode = mode;
        }

        return cfg;
    }
}
