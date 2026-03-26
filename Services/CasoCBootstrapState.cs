namespace CasoC.Services;

internal sealed class CasoCBootstrapState
{
    private CasoCBootstrapSnapshot? _snapshot;

    internal void SetSnapshot(CasoCBootstrapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(ref _snapshot, snapshot);
    }

    internal CasoCBootstrapSnapshot GetRequiredSnapshot()
    {
        return _snapshot ?? throw new InvalidOperationException(
            "Caso C bootstrap has not completed successfully.");
    }
}
