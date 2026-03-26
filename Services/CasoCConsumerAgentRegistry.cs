namespace CasoCConsumer.Services;

internal sealed class CasoCConsumerAgentRegistry
{
    private CasoCConsumerAgentSnapshot? _snapshot;

    internal void SetSnapshot(CasoCConsumerAgentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(ref _snapshot, snapshot);
    }

    internal CasoCConsumerAgentSnapshot GetRequiredSnapshot()
    {
        return _snapshot ?? throw new InvalidOperationException(
            "CasoCConsumer startup validation has not completed successfully.");
    }
}
