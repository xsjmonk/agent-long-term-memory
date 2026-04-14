using HarnessMcp.Contracts;
using HarnessMcp.Core;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class MonitorEventExporter(MonitorRingBuffer buffer) : IMonitorEventExporter
{
    public ValueTask<MonitorBatchDto> GetSinceAsync(long lastSequence, int maxCount, CancellationToken cancellationToken)
    {
        var items = buffer.GetSince(lastSequence, maxCount);
        var toInclusive = items.Count == 0 ? lastSequence : items[^1].Sequence;
        var fromExclusive = lastSequence;
        return ValueTask.FromResult(new MonitorBatchDto(fromExclusive, toInclusive, items));
    }
}
