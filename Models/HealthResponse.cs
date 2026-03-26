namespace CasoC.Models;

public sealed record HealthResponse(
    string Status,
    AgentInfoResponse OrderAgent,
    AgentInfoResponse PolicyAgent,
    AgentInfoResponse PlannerAgent);
