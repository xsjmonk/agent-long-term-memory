using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

/// <summary>
/// Projects raw monitor events into UI-ready slices, trimming long payloads where possible.
/// </summary>
public sealed class UiEventProjector(UiTrimPolicy trim)
{
    public bool IsLog(MonitorEventDto e) => e.EventKind == MonitorEventKind.Log;

    public bool IsOperation(MonitorEventDto e) =>
        e.EventKind is MonitorEventKind.RequestStart or MonitorEventKind.RequestSuccess or MonitorEventKind.RequestFailure;

    public bool IsTiming(MonitorEventDto e) =>
        e.EventKind is MonitorEventKind.SqlTiming or MonitorEventKind.EmbeddingTiming or MonitorEventKind.MergeTiming or MonitorEventKind.ContextPackBuilt;

    public bool IsWarning(MonitorEventDto e) =>
        e.EventKind is MonitorEventKind.Warning or MonitorEventKind.RequestFailure or MonitorEventKind.HealthFailure;

    public bool IsOutput(MonitorEventDto e) =>
        e.EventKind == MonitorEventKind.RequestSuccess && e.PayloadPreviewJson is not null;

    public MonitorEventDto ProjectLog(MonitorEventDto e) =>
        e with { Summary = trim.Trim(e.Summary) };

    public MonitorEventDto ProjectOperation(MonitorEventDto e) =>
        e with { Summary = trim.Trim(e.Summary) };

    public MonitorEventDto ProjectTiming(MonitorEventDto e) =>
        e with { Summary = trim.Trim(e.Summary) };

    public MonitorEventDto ProjectWarning(MonitorEventDto e) =>
        e with
        {
            Level = e.Level ?? "Warning",
            Summary = trim.Trim(e.Summary),
            PayloadPreviewJson = e.PayloadPreviewJson is null ? null : trim.Trim(e.PayloadPreviewJson)
        };

    public MonitorEventDto ProjectOutput(MonitorEventDto e) =>
        e with
        {
            Summary = trim.Trim(e.Summary),
            PayloadPreviewJson = trim.Trim(e.PayloadPreviewJson)
        };
}

