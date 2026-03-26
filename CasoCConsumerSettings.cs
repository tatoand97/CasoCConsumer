namespace CasoCConsumer;

public sealed class CasoCConsumerSettings
{
    public const string SectionName = "CasoCConsumer";

    public string? AzureOpenAiEndpoint { get; init; }

    public string? AzureOpenAiDeployment { get; init; }

    public string? OrderAgentId { get; init; }

    public string? PolicyAgentId { get; init; }

    public string? PlannerAgentId { get; init; }

    public int ResponsesTimeoutSeconds { get; init; } = 60;

    public int ResponsesMaxBackoffSeconds { get; init; } = 8;
}
