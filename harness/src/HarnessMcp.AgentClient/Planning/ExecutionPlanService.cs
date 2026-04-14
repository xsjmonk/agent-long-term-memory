using HarnessMcp.AgentClient.Transport;

namespace HarnessMcp.AgentClient.Planning;

public sealed class ExecutionPlanService
{
    private readonly IPlanningModelClient _model;
    private readonly ExecutionPlanPromptBuilder _promptBuilder;

    public ExecutionPlanService(IPlanningModelClient model, ExecutionPlanPromptBuilder promptBuilder)
    {
        _model = model;
        _promptBuilder = promptBuilder;
    }

    public async Task<ExecutionPlan> SynthesizeAsync(
        string rawTask,
        RequirementIntent requirementIntent,
        RetrievalChunkSet chunkSet,
        string compactMemorySummaryMarkdown,
        CancellationToken cancellationToken)
    {
        var systemPrompt = _promptBuilder.BuildSystemPrompt();
        var userPrompt = _promptBuilder.BuildUserPrompt(rawTask, requirementIntent, chunkSet, compactMemorySummaryMarkdown);

        var json = await _model.CompleteJsonAsync(systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
        return ExecutionPlanParser.Parse(json);
    }
}

