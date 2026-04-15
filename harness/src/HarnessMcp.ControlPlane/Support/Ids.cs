namespace HarnessMcp.ControlPlane.Support;

public static class Ids
{
    public static string NewSessionId() => $"sess-{Guid.NewGuid():N}";

    public static string NewTaskId() => $"task-{Guid.NewGuid():N}";
}