namespace CasoCConsumer.Models;

public sealed record HealthResponse(
    string Status,
    AgentInfoResponse PlannerAgent);
