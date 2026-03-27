using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ClientModel;

namespace CasoCConsumer.Services;

internal sealed class CasoCConsumerStartupValidator
{
    private readonly AIProjectClient _projectClient;
    private readonly CasoCConsumerSettings _settings;
    private readonly ILogger<CasoCConsumerStartupValidator> _logger;

    internal CasoCConsumerStartupValidator(
        AIProjectClient projectClient,
        IOptions<CasoCConsumerSettings> settingsOptions,
        ILogger<CasoCConsumerStartupValidator> logger)
    {
        _projectClient = projectClient;
        _settings = settingsOptions.Value;
        _logger = logger;
    }

    internal async Task<CasoCConsumerAgentSnapshot> ValidateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Startup validation started.");

        await ValidateFoundryProjectAccessAsync(cancellationToken);
        _logger.LogInformation(
            "Foundry project access validated. Endpoint: {Endpoint}",
            _settings.AzureOpenAiEndpoint);

        ValidatedAgentInfo plannerAgent = await ValidateConfiguredAgentAsync(
            _settings.PlannerAgentId!,
            "Planner",
            cancellationToken);

        _logger.LogInformation(
            "Planner agent validated. AgentId: {AgentId}. AgentName: {AgentName}. AgentVersion: {AgentVersion}",
            plannerAgent.Id,
            plannerAgent.Name,
            plannerAgent.Version);

        CasoCConsumerAgentSnapshot snapshot = new(
            plannerAgent,
            TimeSpan.FromSeconds(_settings.ResponsesTimeoutSeconds));

        _logger.LogInformation("Startup validation completed.");
        return snapshot;
    }

    private async Task<ValidatedAgentInfo> ValidateConfiguredAgentAsync(
        string configuredAgentId,
        string agentLabel,
        CancellationToken cancellationToken)
    {
        if (ConfiguredAgentReference.TryParse(configuredAgentId, out ConfiguredAgentReference? reference))
        {
            AgentVersion version = await GetAgentVersionAsync(reference!, agentLabel, cancellationToken);
            return ValidatedAgentInfo.FromAgentVersion(version);
        }

        AgentVersion? versionById = await TryGetAgentVersionByIdAsync(configuredAgentId, cancellationToken);
        if (versionById is not null)
        {
            return ValidatedAgentInfo.FromAgentVersion(versionById);
        }

        throw new InvalidOperationException(
            $"The configured {agentLabel}AgentId '{configuredAgentId}' must resolve to an explicit agent version. Supported formats are an exact version id or '<agent-name>:<version>'.");
    }

    private async Task<AgentVersion> GetAgentVersionAsync(
        ConfiguredAgentReference reference,
        string agentLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            ClientResult<AgentVersion> response = await _projectClient.Agents.GetAgentVersionAsync(
                reference.Name,
                reference.Version,
                cancellationToken);

            return response.Value;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            await foreach (AgentVersion version in _projectClient.Agents.GetAgentVersionsAsync(
                               agentName: reference.Name,
                               cancellationToken: cancellationToken))
            {
                if (string.Equals(version.Id, reference.RawValue, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(version.Version, reference.Version, StringComparison.OrdinalIgnoreCase))
                {
                    return version;
                }
            }

            throw BuildAgentNotFoundException(reference.RawValue, agentLabel, ex);
        }
    }

    private async Task ValidateFoundryProjectAccessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (AgentRecord _ in _projectClient.Agents.GetAgentsAsync(limit: 1, cancellationToken: cancellationToken))
            {
                break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"The configured AzureOpenAiEndpoint '{_settings.AzureOpenAiEndpoint}' is not accessible or is not a valid Foundry project endpoint.",
                ex);
        }
    }

    private async Task<AgentVersion?> TryGetAgentVersionByIdAsync(
        string configuredAgentId,
        CancellationToken cancellationToken)
    {
        await foreach (AgentRecord agent in _projectClient.Agents.GetAgentsAsync(cancellationToken: cancellationToken))
        {
            await foreach (AgentVersion version in _projectClient.Agents.GetAgentVersionsAsync(
                               agentName: agent.Name,
                               cancellationToken: cancellationToken))
            {
                if (string.Equals(version.Id, configuredAgentId, StringComparison.OrdinalIgnoreCase))
                {
                    return version;
                }
            }
        }

        return null;
    }

    private static InvalidOperationException BuildAgentNotFoundException(
        string configuredAgentId,
        string agentLabel,
        Exception innerException)
    {
        return new InvalidOperationException(
            $"The configured {agentLabel}AgentId '{configuredAgentId}' was not found or is not accessible in the Foundry project.",
            innerException);
    }

    private sealed record ConfiguredAgentReference(string RawValue, string Name, string Version)
    {
        internal static bool TryParse(string value, out ConfiguredAgentReference? reference)
        {
            reference = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int separatorIndex = value.LastIndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
            {
                return false;
            }

            string name = value[..separatorIndex].Trim();
            string version = value[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            reference = new ConfiguredAgentReference(value.Trim(), name, version);
            return true;
        }
    }
}
