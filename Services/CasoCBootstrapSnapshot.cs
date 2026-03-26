using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;

namespace CasoC.Services;

internal sealed record CasoCBootstrapSnapshot(
    BootstrapAgentInfo OrderAgent,
    BootstrapAgentInfo PolicyAgent,
    BootstrapAgentInfo PlannerAgent,
    TimeSpan ResponseTimeout);

internal sealed record BootstrapAgentInfo(string Id, string Name, string Version, string ValidationStatus)
{
    internal const string ExternalValidated = "ExternalValidated";

    internal static BootstrapAgentInfo FromAgentVersion(AgentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new BootstrapAgentInfo(
            version.Id,
            version.Name,
            version.Version,
            ExternalValidated);
    }
}
