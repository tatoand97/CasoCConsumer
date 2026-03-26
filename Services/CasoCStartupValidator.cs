using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ClientModel;

namespace CasoC.Services;

internal sealed class CasoCStartupValidator
{
    private readonly AIProjectClient _projectClient;
    private readonly CasoCSettings _settings;
    private readonly ILogger<CasoCStartupValidator> _logger;

    internal CasoCStartupValidator(
        AIProjectClient projectClient,
        IOptions<CasoCSettings> settingsOptions,
        ILogger<CasoCStartupValidator> logger)
    {
        _projectClient = projectClient;
        _settings = settingsOptions.Value;
        _logger = logger;
    }

    internal async Task<CasoCAgentSnapshot> ValidateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Bootstrap validation started.");

        await ValidateFoundryEndpointAsync(cancellationToken);
        _logger.LogInformation("Foundry endpoint validated. Endpoint: {Endpoint}", _settings.AzureOpenAiEndpoint);

        ValidatedAgentInfo orderAgent = await ValidateConfiguredAgentAsync(
            _settings.OrderAgentId!,
            "Order",
            cancellationToken);

        _logger.LogInformation(
            "Order agent validated. AgentId: {AgentId}. AgentName: {AgentName}. AgentVersion: {AgentVersion}. ValidationStatus: {ValidationStatus}",
            orderAgent.Id,
            orderAgent.Name,
            orderAgent.Version,
            orderAgent.ValidationStatus);

        ValidatedAgentInfo policyAgent = await ValidateConfiguredAgentAsync(
            _settings.PolicyAgentId!,
            "Policy",
            cancellationToken);

        _logger.LogInformation(
            "Policy agent validated. AgentId: {AgentId}. AgentName: {AgentName}. AgentVersion: {AgentVersion}. ValidationStatus: {ValidationStatus}",
            policyAgent.Id,
            policyAgent.Name,
            policyAgent.Version,
            policyAgent.ValidationStatus);

        ValidatedAgentInfo plannerAgent = await ValidateConfiguredAgentAsync(
            _settings.PlannerAgentId!,
            "Planner",
            cancellationToken);

        _logger.LogInformation(
            "Planner agent validated. AgentId: {AgentId}. AgentName: {AgentName}. AgentVersion: {AgentVersion}. ValidationStatus: {ValidationStatus}",
            plannerAgent.Id,
            plannerAgent.Name,
            plannerAgent.Version,
            plannerAgent.ValidationStatus);

        CasoCAgentSnapshot snapshot = new(
            orderAgent,
            policyAgent,
            plannerAgent,
            TimeSpan.FromSeconds(_settings.ResponsesTimeoutSeconds));

        _logger.LogInformation("Bootstrap validation completed.");
        return snapshot;
    }

    private async Task<ValidatedAgentInfo> ValidateConfiguredAgentAsync(
        string configuredAgentId,
        string agentLabel,
        CancellationToken cancellationToken)
    {
        ConfiguredAgentReference? reference;
        if (ConfiguredAgentReference.TryParse(configuredAgentId, out reference))
        {
            AgentVersion version = await GetAgentVersionAsync(reference!, agentLabel, cancellationToken);
            return ValidatedAgentInfo.FromAgentVersion(version);
        }

        AgentRecord agent = await GetAgentAsync(configuredAgentId, agentLabel, cancellationToken);
        AgentVersion versionFromName = await GetLatestAgentVersionAsync(
            agent.Name,
            configuredAgentId,
            agentLabel,
            cancellationToken);

        return ValidatedAgentInfo.FromAgentVersion(versionFromName);
    }

    private async Task<AgentRecord> GetAgentAsync(
        string configuredAgentId,
        string agentLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            ClientResult<AgentRecord> response = await _projectClient.Agents.GetAgentAsync(
                configuredAgentId,
                cancellationToken);

            return response.Value;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw BuildAgentNotFoundException(configuredAgentId, agentLabel, ex);
        }
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

    private async Task<AgentVersion> GetLatestAgentVersionAsync(
        string agentName,
        string configuredAgentId,
        string agentLabel,
        CancellationToken cancellationToken)
    {
        await foreach (AgentVersion version in _projectClient.Agents.GetAgentVersionsAsync(
                           agentName: agentName,
                           limit: 1,
                           order: AgentListOrder.Descending,
                           cancellationToken: cancellationToken))
        {
            return version;
        }

        throw new InvalidOperationException(
            $"The configured {agentLabel}AgentId '{configuredAgentId}' resolved to agent '{agentName}', but no accessible versions were found.");
    }

    private async Task ValidateFoundryEndpointAsync(CancellationToken cancellationToken)
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
