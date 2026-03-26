namespace CasoC.Services;

internal sealed class CasoCAgentRegistry
{
    private CasoCAgentSnapshot? _snapshot;

    internal void SetSnapshot(CasoCAgentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(ref _snapshot, snapshot);
    }

    internal CasoCAgentSnapshot GetRequiredSnapshot()
    {
        return _snapshot ?? throw new InvalidOperationException(
            "Caso C startup validation has not completed successfully.");
    }
}
