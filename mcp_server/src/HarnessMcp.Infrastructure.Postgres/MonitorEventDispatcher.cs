using HarnessMcp.Contracts;
using HarnessMcp.Core;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class MonitorEventDispatcher(MonitorRingBuffer buffer, IMonitorEventBroadcaster? realtime) : IMonitorEventSink
{
    public void Publish(MonitorEventDto evt)
    {
        var assigned = buffer.Append(evt);
        if (realtime is null)
            return;
        _ = realtime.BroadcastAsync(assigned, CancellationToken.None);
    }
}
