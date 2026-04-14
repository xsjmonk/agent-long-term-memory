namespace HarnessMcp.AgentClient.Support;

public static class Ids
{
    public static string NewSessionId() => Guid.NewGuid().ToString("N");

    public static string NewTaskId() => $"task:{Guid.NewGuid():N}";

    public static string NewRequestId(string taskId) => $"{taskId}:req:{Guid.NewGuid():N}";
}

