namespace CasoCConsumer.Models;

public sealed record AskDebugResponse(
    string PlannerAnswer,
    string TraceId);
