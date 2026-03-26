namespace CasoC.Services;

internal sealed class CasoCStartupValidationHostedService : IHostedService
{
    private readonly CasoCStartupValidator _startupValidator;
    private readonly CasoCAgentRegistry _agentRegistry;

    internal CasoCStartupValidationHostedService(
        CasoCStartupValidator startupValidator,
        CasoCAgentRegistry agentRegistry)
    {
        _startupValidator = startupValidator;
        _agentRegistry = agentRegistry;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        CasoCAgentSnapshot snapshot = await _startupValidator.ValidateAsync(cancellationToken);
        _agentRegistry.SetSnapshot(snapshot);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
