using Microsoft.Extensions.Logging;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class LogEventFormatter
{
    public string Format(
        DateTimeOffset utcNow,
        LogLevel level,
        string category,
        string message,
        Exception? exception)
    {
        var line = $"{utcNow:O} [{level}] {category}: {message}";
        if (exception is null)
            return line;

        return line + Environment.NewLine + exception;
    }
}

