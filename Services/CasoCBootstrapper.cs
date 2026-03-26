using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ClientModel;

namespace CasoC.Services;

internal sealed class CasoCBootstrapper
{
    private readonly AIProjectClient _projectClient;
    private readonly IOptions<CasoCSettings> _settingsOptions;
    private readonly ILogger<CasoCBootstrapper> _logger;

    internal CasoCBootstrapper(
        AIProjectClient projectClient,
        IOptions<CasoCSettings> settingsOptions,
        ILogger<CasoCBootstrapper> logger)
    {
        _projectClient = projectClient;
        _settingsOptions = settingsOptions;
        _logger = logger;
    }

    internal async Task<CasoCBootstrapSnapshot> BootstrapAsync(CancellationToken cancellationToken)
    {
        CasoCSettings settings = _settingsOptions.Value;
        _logger.LogInformation("Bootstrap validation started.");

        await ValidateIdentityCanAccessProjectAsync(_projectClient, cancellationToken);

        BootstrapAgentInfo orderAgent = await ValidateConfiguredAgentAsync(
            settings.OrderAgentId!,
            "Order",
            cancellationToken);

        _logger.LogInformation(
            "Order agent validated. AgentId: {AgentId}. AgentName: {AgentName}. AgentVersion: {AgentVersion}. ValidationStatus: {ValidationStatus}",
            orderAgent.Id,
            orderAgent.Name,
            orderAgent.Version,
            orderAgent.ValidationStatus);

        BootstrapAgentInfo policyAgent = await ValidateConfiguredAgentAsync(
            settings.PolicyAgentId!,
            "Policy",
            cancellationToken);

        _logger.LogInformation(
            "Policy agent validated. AgentId: {AgentId}. AgentName: {AgentName}. AgentVersion: {AgentVersion}. ValidationStatus: {ValidationStatus}",
            policyAgent.Id,
            policyAgent.Name,
            policyAgent.Version,
            policyAgent.ValidationStatus);

        BootstrapAgentInfo plannerAgent = await ValidateConfiguredAgentAsync(
            settings.PlannerAgentId!,
            "Planner",
            cancellationToken);

        _logger.LogInformation(
            "Planner agent validated. AgentId: {AgentId}. AgentName: {AgentName}. AgentVersion: {AgentVersion}. ValidationStatus: {ValidationStatus}",
            plannerAgent.Id,
            plannerAgent.Name,
            plannerAgent.Version,
            plannerAgent.ValidationStatus);

        CasoCBootstrapSnapshot snapshot = new(
            orderAgent,
            policyAgent,
            plannerAgent,
            TimeSpan.FromSeconds(settings.ResponsesTimeoutSeconds));

        _logger.LogInformation("Bootstrap validation completed.");
        return snapshot;
    }

    private async Task<BootstrapAgentInfo> ValidateConfiguredAgentAsync(
        string configuredAgentId,
        string agentLabel,
        CancellationToken cancellationToken)
    {
        ConfiguredAgentReference? reference;
        if (ConfiguredAgentReference.TryParse(configuredAgentId, out reference))
        {
            AgentVersion version = await GetAgentVersionAsync(reference!, agentLabel, cancellationToken);
            return BootstrapAgentInfo.FromAgentVersion(version);
        }

        AgentRecord agent = await GetAgentAsync(configuredAgentId, agentLabel, cancellationToken);
        AgentVersion versionFromName = await GetLatestAgentVersionAsync(
            agent.Name,
            configuredAgentId,
            agentLabel,
            cancellationToken);

        return BootstrapAgentInfo.FromAgentVersion(versionFromName);
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

    private static async Task ValidateIdentityCanAccessProjectAsync(
        AIProjectClient projectClient,
        CancellationToken cancellationToken)
    {
        await foreach (AgentRecord _ in projectClient.Agents.GetAgentsAsync(limit: 1, cancellationToken: cancellationToken))
        {
            break;
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
