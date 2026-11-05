using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Platform;
using IniaAi.Services.Jobs;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.BackgroundServices;

/// <summary>
/// Agent conversation retention for the deployment, whatever the purpose: ends conversations idle
/// past their purpose's window, deletes ended ones with their turns, drafts, session bundles and
/// run evidence. Turns stuck in flight are the business of the host that runs turns. Runs whether or not the
/// agent runtime is enabled, so turning the runtime off never strands what it left behind. One
/// instance in WebApi covers the deployment.
/// </summary>
public class AgentConversationRetentionService : PeriodicBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentConversationSettings _settings;
    private readonly ILogger<AgentConversationRetentionService> _logger;

    /// <summary>Takes the scope factory each sweep cycle resolves the reaper from.</summary>
    public AgentConversationRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptions<AgentConversationSettings> settings,
        ILogger<AgentConversationRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, _settings.SweepIntervalMinutes));

    /// <inheritdoc/>
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(4);

    /// <inheritdoc/>
    protected override void OnRunFailed(Exception exception)
    {
        _logger.LogError(exception, "AgentConversationRetention: unhandled error in sweep cycle");
    }

    /// <inheritdoc/>
    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IAgentConversationReaper reaper = scope.ServiceProvider.GetRequiredService<IAgentConversationReaper>();

        _ = await reaper.SweepAsync(cancellationToken);
    }
}
