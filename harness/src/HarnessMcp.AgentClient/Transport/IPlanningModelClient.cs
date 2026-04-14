namespace HarnessMcp.AgentClient.Transport;

public interface IPlanningModelClient
{
    Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);
}

