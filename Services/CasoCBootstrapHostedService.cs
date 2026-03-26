namespace CasoC.Services;

internal sealed class CasoCBootstrapHostedService : IHostedService
{
    private readonly CasoCBootstrapper _bootstrapper;
    private readonly CasoCBootstrapState _bootstrapState;

    internal CasoCBootstrapHostedService(
        CasoCBootstrapper bootstrapper,
        CasoCBootstrapState bootstrapState)
    {
        _bootstrapper = bootstrapper;
        _bootstrapState = bootstrapState;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        CasoCBootstrapSnapshot snapshot = await _bootstrapper.BootstrapAsync(cancellationToken);
        _bootstrapState.SetSnapshot(snapshot);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
