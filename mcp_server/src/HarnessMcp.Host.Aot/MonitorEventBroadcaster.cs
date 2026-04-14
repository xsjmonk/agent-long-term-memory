using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using Microsoft.AspNetCore.SignalR;

namespace HarnessMcp.Host.Aot;

public sealed class MonitorEventBroadcaster : IMonitorEventBroadcaster
{
    private IHubContext<MonitoringHub>? _hub;

    public void Attach(IHubContext<MonitoringHub> hub) => _hub = hub;

    public ValueTask BroadcastAsync(MonitorEventDto evt, CancellationToken cancellationToken)
    {
        var hub = _hub;
        if (hub is null)
            return ValueTask.CompletedTask;

        return new ValueTask(hub.Clients.All.SendAsync(
            "monitor",
            JsonSerializer.Serialize(evt, AppJsonSerializerContext.Default.MonitorEventDto),
            cancellationToken));
    }
}
