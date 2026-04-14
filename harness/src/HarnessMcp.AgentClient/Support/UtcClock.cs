namespace HarnessMcp.AgentClient.Support;

public static class UtcClock
{
    public static DateTimeOffset NowUtc() => DateTimeOffset.UtcNow;
}

