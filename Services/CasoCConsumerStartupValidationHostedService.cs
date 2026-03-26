namespace CasoCConsumer.Services;

internal sealed class CasoCConsumerStartupValidationHostedService : IHostedService
{
    private readonly CasoCConsumerStartupValidator _startupValidator;
    private readonly CasoCConsumerAgentRegistry _agentRegistry;

    internal CasoCConsumerStartupValidationHostedService(
        CasoCConsumerStartupValidator startupValidator,
        CasoCConsumerAgentRegistry agentRegistry)
    {
        _startupValidator = startupValidator;
        _agentRegistry = agentRegistry;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        CasoCConsumerAgentSnapshot snapshot = await _startupValidator.ValidateAsync(cancellationToken);
        _agentRegistry.SetSnapshot(snapshot);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
