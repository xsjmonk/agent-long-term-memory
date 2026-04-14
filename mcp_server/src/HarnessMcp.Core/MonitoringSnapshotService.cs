using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class MonitoringSnapshotService(
    AppConfig config,
    IAppInfoProvider appInfo,
    IMonitorEventBuffer buffer,
    UiEventProjector projector,
    DateTimeOffset startedAtUtc) : IMonitoringSnapshotService
{
    public ValueTask<MonitorSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var info = appInfo.GetServerInfo();
        var summary = new MonitorServerSummaryDto(
            info.ServerName,
            info.ServerVersion,
            info.ProtocolMode,
            MonitoringEnabled: config.Server.EnableMonitoringUi,
            RealtimeEnabled: config.Monitoring.EnableRealtimeUi,
            startedAtUtc,
            config.Server.Environment,
            DatabaseConfigured: !string.IsNullOrWhiteSpace(config.Database.Host),
            EmbeddingProviderSummary: $"{config.Embedding.QueryEmbeddingProvider} model={config.Embedding.Model}");

        var events = buffer.Snapshot();
        var max = config.Monitoring.MaxRenderedRows;

        List<MonitorEventDto> TakeLast(Func<MonitorEventDto, bool> predicate)
        {
            var list = events.Where(predicate).ToList();
            if (list.Count <= max) return list;
            return list.Skip(list.Count - max).ToList();
        }

        var recentLogs = TakeLast(projector.IsLog).Select(projector.ProjectLog).ToList();
        var recentOps = TakeLast(projector.IsOperation).Select(projector.ProjectOperation).ToList();
        var recentTimings = TakeLast(projector.IsTiming).Select(projector.ProjectTiming).ToList();
        var recentWarnings = TakeLast(projector.IsWarning).Select(projector.ProjectWarning).ToList();
        var recentOutputs = TakeLast(projector.IsOutput).Select(projector.ProjectOutput).ToList();

        return ValueTask.FromResult(new MonitorSnapshotDto(
            summary,
            recentLogs,
            recentOps,
            recentTimings,
            recentWarnings,
            recentOutputs,
            buffer.LastSequence));
    }
}

