using Microsoft.Extensions.Logging;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class RotatingFileLogger : ILogger
{
    private readonly string _category;
    private readonly LogFileRoller _roller;
    private readonly LogEventFormatter _formatter;
    private readonly Func<bool> _enabled;

    public RotatingFileLogger(
        string category,
        LogFileRoller roller,
        LogEventFormatter formatter,
        Func<bool>? enabled = null)
    {
        _category = category;
        _roller = roller;
        _formatter = formatter;
        _enabled = enabled ?? (() => true);
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && _enabled();

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message))
            return;

        var path = _roller.EnsureCurrentFile();
        var line = _formatter.Format(DateTimeOffset.UtcNow, logLevel, _category, message, exception);
        try
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Avoid crashing the host due to logging failures.
        }
    }
}

