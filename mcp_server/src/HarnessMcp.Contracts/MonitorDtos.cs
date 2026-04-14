namespace HarnessMcp.Contracts;

public sealed record MonitorEventDto(
    long Sequence,
    DateTimeOffset TimestampUtc,
    MonitorEventKind EventKind,
    string? RequestId,
    string? ToolName,
    string? TaskId,
    string? Level,
    string Summary,
    string? PayloadPreviewJson);

public sealed record MonitorBatchDto(
    long FromExclusiveSequence,
    long ToInclusiveSequence,
    IReadOnlyList<MonitorEventDto> Events);

public sealed record MonitorServerSummaryDto(
    string ServerName,
    string ServerVersion,
    string ProtocolMode,
    bool MonitoringEnabled,
    bool RealtimeEnabled,
    DateTimeOffset StartedUtc,
    string Environment,
    bool DatabaseConfigured,
    string EmbeddingProviderSummary);

public sealed record MonitorSnapshotDto(
    MonitorServerSummaryDto Server,
    IReadOnlyList<MonitorEventDto> RecentLogs,
    IReadOnlyList<MonitorEventDto> RecentOperations,
    IReadOnlyList<MonitorEventDto> RecentTimings,
    IReadOnlyList<MonitorEventDto> RecentWarnings,
    IReadOnlyList<MonitorEventDto> RecentOutputs,
    long LastSequence);
