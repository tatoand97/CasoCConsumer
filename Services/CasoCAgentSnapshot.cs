using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;

namespace CasoC.Services;

internal sealed record CasoCAgentSnapshot(
    ValidatedAgentInfo OrderAgent,
    ValidatedAgentInfo PolicyAgent,
    ValidatedAgentInfo PlannerAgent,
    TimeSpan ResponseTimeout);

internal sealed record ValidatedAgentInfo(string Id, string Name, string Version, string ValidationStatus)
{
    internal const string ExternalValidated = "ExternalValidated";

    internal static ValidatedAgentInfo FromAgentVersion(AgentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new ValidatedAgentInfo(
            version.Id,
            version.Name,
            version.Version,
            ExternalValidated);
    }
}
