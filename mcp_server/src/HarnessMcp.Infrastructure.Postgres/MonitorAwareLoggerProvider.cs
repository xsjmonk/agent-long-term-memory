using System.Collections.Concurrent;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using Microsoft.Extensions.Logging;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class MonitorAwareLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, MonitorAwareLogger> _loggers = new();
    private readonly Func<bool> _monitoringEnabled;
    private readonly IMonitorEventSink _sink;
    private readonly UiLogProjector _ui;

    public MonitorAwareLoggerProvider(Func<bool> monitoringEnabled, IMonitorEventSink sink, UiLogProjector uiLogProjector)
    {
        _monitoringEnabled = monitoringEnabled;
        _sink = sink;
        _ui = uiLogProjector;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new MonitorAwareLogger(name, _monitoringEnabled, _sink, _ui));

    public void Dispose() => _loggers.Clear();
}

public sealed class MonitorAwareLogger(string category, Func<bool> enabled, IMonitorEventSink sink, UiLogProjector ui) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var msg = formatter(state, exception);
        if (!enabled())
            return;

        var payload = exception is null ? null : ui.TrimNullable(exception.ToString());

        sink.Publish(new MonitorEventDto(
            0,
            DateTimeOffset.UtcNow,
            MonitorEventKind.Log,
            null,
            category,
            null,
            logLevel.ToString(),
            ui.Trim(msg),
            payload));
    }
}
