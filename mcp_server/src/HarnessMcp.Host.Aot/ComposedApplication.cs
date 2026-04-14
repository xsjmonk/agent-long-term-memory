using HarnessMcp.Contracts;
using HarnessMcp.Core;
using HarnessMcp.Transport.Mcp;
using Npgsql;
using Microsoft.Extensions.Logging;

namespace HarnessMcp.Host.Aot;

public sealed record ComposedApplication(
    AppConfig Config,
    NpgsqlDataSource DataSource,
    IHealthProbe HealthProbe,
    KnowledgeQueryTools KnowledgeQueryTools,
    KnowledgeResources KnowledgeResources,
    IMonitorEventSink MonitorEventSink,
    IMonitorEventExporter MonitorEventExporter,
    IMonitoringSnapshotService MonitoringSnapshotService,
    IMonitorEventBroadcaster MonitorEventBroadcaster,
    IAppInfoProvider AppInfoProvider,
    ILoggerFactory LoggerFactory);
