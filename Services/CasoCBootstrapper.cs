using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using CasoC.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ClientModel;

namespace CasoC.Services;

internal sealed class CasoCBootstrapper
{
    private readonly AIProjectClient _projectClient;
    private readonly AgentReconciler _reconciler;
    private readonly IOptions<CasoCSettings> _settingsOptions;
    private readonly ILogger<CasoCBootstrapper> _logger;

    internal CasoCBootstrapper(
        AIProjectClient projectClient,
        AgentReconciler reconciler,
        IOptions<CasoCSettings> settingsOptions,
        ILogger<CasoCBootstrapper> logger)
    {
        _projectClient = projectClient;
        _reconciler = reconciler;
        _settingsOptions = settingsOptions;
        _logger = logger;
    }

    internal async Task<CasoCBootstrapSnapshot> BootstrapAsync(CancellationToken cancellationToken)
    {
        CasoCSettings settings = _settingsOptions.Value;
        _logger.LogInformation("Bootstrap started.");

        await ValidateIdentityCanAccessProjectAsync(_projectClient, cancellationToken);

        AgentRecord orderAgent = await ValidateOrderAgentAsync(
            _projectClient,
            settings.OrderAgentId!,
            cancellationToken);

        AgentVersion orderAgentVersion = await ResolveOrderAgentVersionAsync(
            _projectClient,
            orderAgent.Name,
            settings.OrderAgentVersion,
            cancellationToken);

        _logger.LogInformation(
            "Order agent validated. AgentId: {AgentId}. AgentName: {AgentName}. AgentVersion: {AgentVersion}",
            orderAgentVersion.Id,
            orderAgentVersion.Name,
            orderAgentVersion.Version);

        ReconcileResult policyResult = await _reconciler.ReconcileAsync(
            PolicyAgentFactory.AgentName,
            PolicyAgentFactory.Build(settings.AzureOpenAiDeployment!),
            cancellationToken);

        _logger.LogInformation(
            "Policy agent reconciled. AgentId: {AgentId}. AgentName: {AgentName}. AgentVersion: {AgentVersion}. ReconciliationStatus: {ReconciliationStatus}",
            policyResult.Version.Id,
            policyResult.Version.Name,
            policyResult.Version.Version,
            policyResult.ReconciliationStatus);

        ReconcileResult plannerResult = await _reconciler.ReconcileAsync(
            PlannerAgentFactory.AgentName,
            PlannerAgentFactory.Build(settings.AzureOpenAiDeployment!),
            cancellationToken);

        _logger.LogInformation(
            "Planner agent reconciled. AgentId: {AgentId}. AgentName: {AgentName}. AgentVersion: {AgentVersion}. ReconciliationStatus: {ReconciliationStatus}",
            plannerResult.Version.Id,
            plannerResult.Version.Name,
            plannerResult.Version.Version,
            plannerResult.ReconciliationStatus);

        CasoCBootstrapSnapshot snapshot = new(
            BootstrapAgentInfo.FromAgentVersion(orderAgentVersion),
            BootstrapAgentInfo.FromAgentVersion(policyResult.Version),
            BootstrapAgentInfo.FromAgentVersion(plannerResult.Version),
            TimeSpan.FromSeconds(settings.ResponsesTimeoutSeconds));

        _logger.LogInformation("Bootstrap completed.");
        return snapshot;
    }

    private static async Task<AgentRecord> ValidateOrderAgentAsync(
        AIProjectClient projectClient,
        string orderAgentId,
        CancellationToken cancellationToken)
    {
        try
        {
            ClientResult<AgentRecord> response = await projectClient.Agents.GetAgentAsync(
                orderAgentId,
                cancellationToken);

            return response.Value;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"The configured OrderAgentId '{orderAgentId}' was not found or is not accessible in the Foundry project.",
                ex);
        }
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

    private static async Task<AgentVersion> ResolveOrderAgentVersionAsync(
        AIProjectClient projectClient,
        string agentName,
        string? versionSetting,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(versionSetting) ||
            string.Equals(versionSetting, "latest", StringComparison.OrdinalIgnoreCase))
        {
            await foreach (AgentVersion version in projectClient.Agents.GetAgentVersionsAsync(
                               agentName: agentName,
                               limit: 1,
                               order: AgentListOrder.Descending,
                               cancellationToken: cancellationToken))
            {
                return version;
            }

            throw new InvalidOperationException(
                $"No version was found for the order agent '{agentName}'.");
        }

        await foreach (AgentVersion version in projectClient.Agents.GetAgentVersionsAsync(
                           agentName: agentName,
                           cancellationToken: cancellationToken))
        {
            if (string.Equals(version.Name, versionSetting, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(version.Id, versionSetting, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(version.Version, versionSetting, StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }
        }

        throw new InvalidOperationException(
            $"The configured order agent version '{versionSetting}' was not found for agent '{agentName}'.");
    }
}
