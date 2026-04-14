using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class AppInfoProvider(AppConfig config, string serverVersion, string protocolMode) : IAppInfoProvider
{
    public ServerInfoResponse GetServerInfo()
    {
        var version = serverVersion;
        var features = new FeatureFlagsDto(
            RetrieveMemoryByChunks: true,
            MergeRetrievalResults: true,
            BuildMemoryContextPack: true,
            SearchKnowledge: true,
            GetKnowledgeItem: true,
            GetRelatedKnowledge: true,
            HttpTransport: config.Server.TransportMode == TransportMode.Http,
            StdioTransport: config.Server.TransportMode == TransportMode.Stdio,
            WriteOperations: false,
            MonitoringUi: config.Server.EnableMonitoringUi,
            RealtimeTracking: config.Monitoring.EnableRealtimeUi);

        var schemas = new SchemaSetDto(
            SchemaConstants.CurrentSchemaVersion,
            SchemaConstants.CurrentSchemaVersion,
            SchemaConstants.CurrentSchemaVersion,
            SchemaConstants.CurrentSchemaVersion,
            SchemaConstants.CurrentSchemaVersion,
            SchemaConstants.CurrentSchemaVersion,
            SchemaConstants.CurrentSchemaVersion);

        return new ServerInfoResponse(
            SchemaConstants.CurrentSchemaVersion,
            "server_info",
            "HarnessMcp",
            version,
            protocolMode,
            features,
            schemas);
    }
}
