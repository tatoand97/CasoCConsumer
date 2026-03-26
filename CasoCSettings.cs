namespace CasoC;

public sealed class CasoCSettings
{
    public const string SectionName = "CasoC";

    public string? AzureOpenAiEndpoint { get; init; }

    public string? AzureOpenAiDeployment { get; init; }

    public string? OrderAgentId { get; init; }

    public string? PolicyAgentId { get; init; }

    public string? PlannerAgentId { get; init; }

    public int ResponsesTimeoutSeconds { get; init; } = 60;

    public int ResponsesMaxBackoffSeconds { get; init; } = 8;
}
