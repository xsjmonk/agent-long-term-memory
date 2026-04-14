using HarnessMcp.Contracts;
using HarnessMcp.Core;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class MonitorRingBuffer(int capacity) : IMonitorEventBuffer
{
    private readonly object _lock = new();
    private long _sequence;
    private readonly Queue<MonitorEventDto> _queue = new();

    public MonitorEventDto Append(MonitorEventDto evt)
    {
        lock (_lock)
        {
            _sequence++;
            var assigned = evt with { Sequence = _sequence };
            _queue.Enqueue(assigned);
            while (_queue.Count > capacity)
                _queue.Dequeue();
            return assigned;
        }
    }

    public IReadOnlyList<MonitorEventDto> GetSince(long afterSequence, int maxCount)
    {
        lock (_lock)
        {
            return _queue.Where(e => e.Sequence > afterSequence).Take(maxCount).ToList();
        }
    }

    public IReadOnlyList<MonitorEventDto> Snapshot()
    {
        lock (_lock)
        {
            return _queue.ToList();
        }
    }

    public long LastSequence
    {
        get
        {
            lock (_lock)
            {
                return _sequence;
            }
        }
    }
}
