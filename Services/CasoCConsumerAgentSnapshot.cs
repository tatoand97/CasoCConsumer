using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;

namespace CasoCConsumer.Services;

internal sealed record CasoCConsumerAgentSnapshot(
    ValidatedAgentInfo PlannerAgent,
    TimeSpan ResponseTimeout);

internal sealed record ValidatedAgentInfo(string Id, string Name, string Version)
{
    internal static ValidatedAgentInfo FromAgentVersion(AgentVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new ValidatedAgentInfo(
            version.Id,
            version.Name,
            version.Version);
    }
}
