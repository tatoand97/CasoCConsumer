using Azure.AI.Projects.OpenAI;

namespace CasoCConsumer.Services;

internal sealed class PlannerAgentConsumer
{
    private readonly ProjectOpenAIClient _openAiClient;
    private readonly AgentRunner _runner;
    private readonly CasoCConsumerAgentRegistry _agentRegistry;

    internal PlannerAgentConsumer(
        ProjectOpenAIClient openAiClient,
        AgentRunner runner,
        CasoCConsumerAgentRegistry agentRegistry)
    {
        _openAiClient = openAiClient;
        _runner = runner;
        _agentRegistry = agentRegistry;
    }

    internal async Task<string> AskAsync(string prompt, CancellationToken cancellationToken)
    {
        CasoCConsumerAgentSnapshot snapshot = _agentRegistry.GetRequiredSnapshot();

        return await _runner.RunPromptAsync(
            _openAiClient,
            snapshot.PlannerAgent,
            prompt,
            snapshot.ResponseTimeout,
            cancellationToken);
    }
}
