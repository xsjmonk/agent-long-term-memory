using HarnessMcp.AgentClient.Transport;

namespace HarnessMcp.AgentClient.Planning;

public sealed class RequirementInterpretationService
{
    private readonly IPlanningModelClient _modelClient;

    public RequirementInterpretationService(IPlanningModelClient modelClient)
    {
        _modelClient = modelClient;
    }

    public async Task<RequirementIntent> InterpretAsync(
        string sessionId,
        string taskId,
        string rawTask,
        string? project,
        string? domain,
        CancellationToken cancellationToken)
    {
        var systemPrompt = RequirementIntentPromptBuilder.BuildSystemPrompt();
        var userPrompt = RequirementIntentPromptBuilder.BuildUserPrompt(rawTask, project, domain);

        var json = await _modelClient.CompleteJsonAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
        return RequirementIntentParser.Parse(sessionId, taskId, rawTask, json);
    }
}

