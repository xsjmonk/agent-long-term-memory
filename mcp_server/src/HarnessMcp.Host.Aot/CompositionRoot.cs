using System.Reflection;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using HarnessMcp.Infrastructure.Postgres;
using HarnessMcp.Transport.Mcp;
using Microsoft.Extensions.Logging;

namespace HarnessMcp.Host.Aot;

public static class CompositionRoot
{
    public static ComposedApplication Build(AppConfig config)
    {
        ValidateConfig(config);

        var ring = new MonitorRingBuffer(config.Monitoring.RingBufferSize);
        var broadcaster = new MonitorEventBroadcaster();
        var sink = new MonitorEventDispatcher(ring, broadcaster);
        var exporter = new MonitorEventExporter(ring);
        var contextStore = new InMemorySearchRequestContextStore();

        var loggerFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(ParseLevel(config.Logging.Level));
            b.AddConsole();
        });

        var dataSource = NpgsqlDataSourceFactory.Create(config.Database);

        var health = new ConnectionHealthProbe(dataSource);
        var repository = new PostgresKnowledgeRepository(config, dataSource);
        var caseShape = new PostgresCaseShapeScoreProvider(dataSource, config.Database.SearchSchema);

        IQueryEmbeddingService embedding = string.Equals(config.Embedding.QueryEmbeddingProvider, "NoOp", StringComparison.OrdinalIgnoreCase)
            ? new NoOpQueryEmbeddingService()
            : string.Equals(config.Embedding.QueryEmbeddingProvider, "LocalHttp", StringComparison.OrdinalIgnoreCase)
                ? new LocalHttpQueryEmbeddingService(config.Embedding, null, contextStore)
                : throw new InvalidOperationException($"Unknown Embedding.QueryEmbeddingProvider='{config.Embedding.QueryEmbeddingProvider}'");

        var embeddingInspector = new EmbeddingMetadataInspector(config, dataSource);
        var embeddingCompatibility = new EmbeddingCompatibilityChecker();

        var validator = new RequestValidator(config.Retrieval);
        var authority = new AuthorityPolicy();
        var scopeNorm = new ScopeNormalizer();
        var planner = new ChunkQueryPlanner();
        var ranking = new HybridRankingService(authority, caseShape, contextStore);
        var assembler = new ContextPackAssembler();
        var cache = new InMemoryContextPackCache();

        var search = new KnowledgeSearchService(
            validator,
            scopeNorm,
            repository,
            embedding,
            ranking,
            config.Embedding,
            embeddingInspector,
            embeddingCompatibility);
        var chunk = new ChunkRetrievalService(validator, planner, search, contextStore);
        var merge = new RetrievalMergeService(validator);
        var pack = new MemoryContextPackService(validator, assembler, cache, config.Features);
        var read = new KnowledgeReadService(validator, repository);
        var related = new RelatedKnowledgeService(validator, repository);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        var protocol = config.Server.IsHttp() ? "http+mcp" : "stdio+mcp";
        var appInfo = new AppInfoProvider(config, version, protocol);

        var trim = new UiTrimPolicy(config.Monitoring);
        var projector = new UiEventProjector(trim);
        var started = DateTimeOffset.UtcNow;
        var snapshot = new MonitoringSnapshotService(config, appInfo, ring, projector, started);

        var tools = new KnowledgeQueryTools(
            chunk,
            merge,
            pack,
            search,
            read,
            related,
            appInfo,
            sink,
            config.Monitoring.MaxPayloadPreviewChars);

        var schema = new JsonSchemaDocumentProvider();
        var resources = new KnowledgeResources(read, cache, schema);

        var composed = new ComposedApplication(
            config,
            dataSource,
            health,
            tools,
            resources,
            sink,
            exporter,
            snapshot,
            broadcaster,
            appInfo,
            loggerFactory);

        return composed;
    }

    private static void ValidateConfig(AppConfig config)
    {
        if (config.Retrieval.MaxTopK <= 0)
            throw new InvalidOperationException("Retrieval.MaxTopK invalid.");
        if (config.Monitoring.RingBufferSize <= 0)
            throw new InvalidOperationException("Monitoring.RingBufferSize invalid.");
    }

    private static LogLevel ParseLevel(string level) =>
        Enum.TryParse<LogLevel>(level, true, out var l) ? l : LogLevel.Information;
}
