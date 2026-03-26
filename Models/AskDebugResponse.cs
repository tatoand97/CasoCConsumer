namespace CasoC.Models;

public sealed record AskDebugResponse(
    string OrderContext,
    string PolicyContext,
    string FinalAnswer,
    string TraceId);
