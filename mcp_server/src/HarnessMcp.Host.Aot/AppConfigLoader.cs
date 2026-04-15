using Microsoft.Extensions.Configuration;
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

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(fullPath, optional: false, reloadOnChange: false)
            .Build();

        var cfg = new AppConfig();
        configuration.GetSection("Server").Bind(cfg.Server);
        configuration.GetSection("Database").Bind(cfg.Database);
        configuration.GetSection("Retrieval").Bind(cfg.Retrieval);
        configuration.GetSection("Embedding").Bind(cfg.Embedding);
        configuration.GetSection("Logging").Bind(cfg.Logging);
        configuration.GetSection("Monitoring").Bind(cfg.Monitoring);
        configuration.GetSection("Features").Bind(cfg.Features);

        // Apply command-line override after file is loaded.
        if (args.Length > 0 && string.Equals(args[0], "--transport", StringComparison.OrdinalIgnoreCase) && args.Length > 1)
        {
            if (Enum.TryParse<TransportMode>(args[1], ignoreCase: true, out var mode))
                cfg.Server.TransportMode = mode;
        }

        return cfg;
    }
}
