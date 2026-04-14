using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using Microsoft.AspNetCore.Builder;

namespace HarnessMcp.Host.Aot;

public static class MonitoringWebEndpoints
{
    public static void MapMonitoring(
        this WebApplication app,
        ComposedApplication composed,
        bool monitoringUiEnabled,
        bool realtimeEnabled)
    {
        // In HTTP mode, the events export endpoint is always available.
        app.MapGet("/monitor/events", async (long? after, int? take, CancellationToken ct) =>
        {
            var batch = await composed.MonitorEventExporter
                .GetSinceAsync(after ?? 0, take ?? composed.Config.Monitoring.EventExportDefaultTake, ct)
                .ConfigureAwait(false);
            return Results.Json(batch, AppJsonSerializerContext.Default.MonitorBatchDto);
        });

        if (!monitoringUiEnabled)
            return;

        app.MapGet("/monitor", () => Results.Content(StaticMonitoringPageProvider.GetHtml(composed.Config), "text/html"));

        app.MapGet("/monitor/snapshot", async (CancellationToken ct) =>
        {
            var snap = await composed.MonitoringSnapshotService.GetSnapshotAsync(ct).ConfigureAwait(false);
            return Results.Json(snap, AppJsonSerializerContext.Default.MonitorSnapshotDto);
        });

        if (realtimeEnabled)
            app.MapHub<MonitoringHub>("/monitor/hub");
    }
}
