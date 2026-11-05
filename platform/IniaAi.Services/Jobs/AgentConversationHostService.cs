using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Services.Jobs;

/// <summary>
/// The conversation turn housekeeping of the host that runs turns. At startup: turns this host
/// dispatched and the database still shows in flight belonged to the process that stopped, so
/// they are settled Cancelled and their runs cancelled and billed; then every waiting turn whose
/// predecessors have settled starts. Every <see cref="AgentConversationSettings.TurnSweepIntervalMinutes"/>
/// after that: turns in flight past their deadline are failed, and waiting turns nobody started
/// (their predecessor was settled by another host or by the overdue sweep) start.
/// </summary>
public class AgentConversationHostService : PeriodicBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentConversationSettings _settings;
    private readonly ILogger<AgentConversationHostService> _logger;
    private bool _recovered;

    public AgentConversationHostService(
        IServiceScopeFactory scopeFactory,
        IOptions<AgentConversationSettings> settings,
        ILogger<AgentConversationHostService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, _settings.TurnSweepIntervalMinutes));

    /// <summary>
    /// The first run settles what the stopped process left in flight; every later run is the sweep.
    /// </summary>
    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        if (_recovered)
        {
            await SweepAsync(scope.ServiceProvider, cancellationToken);
            return;
        }

        _recovered = true;
        await RecoverAsync(scope.ServiceProvider, cancellationToken);
    }

    protected override void OnRunFailed(Exception exception)
    {
        _logger.LogError(exception, "Agent conversation turn housekeeping failed");
    }

    private static async Task RecoverAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        _ = await services.GetRequiredService<IAgentConversationReaper>().AbandonInFlightTurnsAsync(cancellationToken);
        _ = await services.GetRequiredService<IAgentConversationTurns>().DispatchReadyAsync(cancellationToken);
    }

    private static async Task SweepAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        _ = await services.GetRequiredService<IAgentConversationReaper>().SettleOverdueTurnsAsync(cancellationToken);
        _ = await services.GetRequiredService<IAgentConversationTurns>().DispatchReadyAsync(cancellationToken);
    }
}
