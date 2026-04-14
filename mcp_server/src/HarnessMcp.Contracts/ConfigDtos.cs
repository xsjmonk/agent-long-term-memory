namespace HarnessMcp.Contracts;

public sealed class AppConfig
{
    public ServerApiConfig ServerApi { get; set; } = new();
    public ServerConfig Server { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public RetrievalConfig Retrieval { get; set; } = new();
    public EmbeddingConfig Embedding { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
    public MonitoringConfig Monitoring { get; set; } = new();
    public FeatureConfig Features { get; set; } = new();
}

public sealed class ServerApiConfig
{
    public string McpEndpoint { get; set; } = "http://127.0.0.1:5081";
    public string EmbeddingApi { get; set; } = "";
}

public sealed class ServerConfig
{
    public TransportMode TransportMode { get; set; } = TransportMode.Http;
    public string HttpListenUrl { get; set; } = "http://127.0.0.1:5081";
    public string Environment { get; set; } = "Production";
    public bool EnableMonitoringUi { get; set; }
}

public sealed class DatabaseConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "mcp";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "";
    public string SearchSchema { get; set; } = "public";
    public int CommandTimeoutSeconds { get; set; } = 30;
}

public sealed class RetrievalConfig
{
    public int DefaultTopK { get; set; } = 5;
    public int MaxTopK { get; set; } = 50;
    public AuthorityLevel MinimumAuthority { get; set; } = AuthorityLevel.Reviewed;
    public int LexicalCandidateCount { get; set; } = 200;
    public int SemanticCandidateCount { get; set; } = 200;
    public string EmbeddingRole { get; set; } = "CoreTask";
    public int MaxQueryTextLength { get; set; } = 2048;
    public int MaxChunkTextLength { get; set; } = 2048;
}

public sealed class EmbeddingConfig
{
    public string QueryEmbeddingProvider { get; set; } = "NoOp";
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public bool RequireCompatibilityCheck { get; set; } = true;
    public bool AllowLexicalFallbackOnSemanticIncompatibility { get; set; } = true;
    public bool AllowHashingFallback { get; set; } = false;

    // Optional expected metadata exposed by the builder-API. Used to surface semantic quality degradation.
    public string? ExpectedTextProcessingId { get; set; }
    public string? ExpectedVectorSpaceId { get; set; }
    public bool TreatTextProcessingMismatchAsIncompatible { get; set; } = false;
    public bool TreatVectorSpaceMismatchAsIncompatible { get; set; } = false;
}

public sealed class LoggingConfig
{
    public string Level { get; set; } = "Information";
    public string Directory { get; set; } = "./logs";
    public string FileNamePrefix { get; set; } = "harness-mcp";
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxRetainedFiles { get; set; } = 10;
    public bool ForwardToMonitor { get; set; } = true;
}

public sealed class MonitoringConfig
{
    public bool EnableEventExport { get; set; } = true;
    public bool EnableRealtimeUi { get; set; } = true;
    public int RingBufferSize { get; set; } = 5000;
    public int EventExportDefaultTake { get; set; } = 200;
    public int MaxRenderedRows { get; set; } = 2000;
    public int MaxPayloadPreviewChars { get; set; } = 4000;
}

public sealed class FeatureConfig
{
    public bool EnableContextPackCache { get; set; } = true;
    public bool EnableRawDetails { get; set; }
    public bool EnableStructureChannel { get; set; }
}
