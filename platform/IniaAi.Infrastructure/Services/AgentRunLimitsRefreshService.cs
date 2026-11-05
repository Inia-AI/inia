using IniaAi.Common.Interfaces.Services.AgentRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Keeps <see cref="AgentRunLimitsCache"/> filled: one fetch at host startup, then a periodic
/// refresh, with a shorter retry after a failed fetch so an outage ends within about a minute of
/// the runtime coming back. Registered only when the agent runtime is enabled — the cache itself
/// is registered always, so consumers resolve on a disabled host and simply see nothing known.
/// Failures never surface past a log line: a save must not fail because discovery did, and the
/// cache's freshness window decides when staleness starts to matter.
/// </summary>
public class AgentRunLimitsRefreshService : BackgroundService
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FailedFetchRetryInterval = TimeSpan.FromMinutes(1);

    private readonly AgentRunLimitsCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRunLimitsRefreshService> _logger;

    public AgentRunLimitsRefreshService(
        AgentRunLimitsCache cache,
        IServiceScopeFactory scopeFactory,
        ILogger<AgentRunLimitsRefreshService> logger)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool refreshed = await RefreshOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(refreshed ? RefreshInterval : FailedFetchRetryInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// A scope per fetch: the typed HTTP client is transient, and capturing one instance for the
    /// host's lifetime would pin its handler past the handler pool's rotation.
    /// </summary>
    internal async Task<bool> RefreshOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAgentRuntimeClient client = scope.ServiceProvider.GetRequiredService<IAgentRuntimeClient>();

            ErrorOr.ErrorOr<AgentRunLimits> limits = await client.GetLimitsAsync(cancellationToken);
            if (limits.IsError)
            {
                _logger.LogWarning(
                    "Agent runtime limits refresh failed: {Error}", limits.FirstError.Description);
                return false;
            }

            _cache.Store(limits.Value);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Agent runtime limits refresh failed unexpectedly");
            return false;
        }
    }
}
