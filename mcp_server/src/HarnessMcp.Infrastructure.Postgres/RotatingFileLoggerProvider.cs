using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class RotatingFileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, RotatingFileLogger> _loggers = new();
    private readonly LogFileRoller _roller;
    private readonly LogEventFormatter _formatter;
    private readonly Func<bool> _enabled;

    public RotatingFileLoggerProvider(
        LogFileRoller roller,
        LogEventFormatter formatter,
        Func<bool>? enabled = null)
    {
        _roller = roller;
        _formatter = formatter;
        _enabled = enabled ?? (() => true);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName,
            name => new RotatingFileLogger(name, _roller, _formatter, _enabled));

    public void Dispose() => _loggers.Clear();
}

