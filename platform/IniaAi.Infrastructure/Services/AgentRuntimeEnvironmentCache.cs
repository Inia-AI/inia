using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The process-wide holder of the runtime's environment text. The text changes only with a
/// runtime deployment, so one fetch serves every editor panel for <see cref="AvailableTtl"/>;
/// a missing text is retried sooner, so a runtime upgraded to serve it shows up within
/// <see cref="NotAvailableTtl"/>. Concurrent callers share the fetch in flight. On a host
/// with agents disabled no client is registered and the answer is always not available.
/// </summary>
public sealed class AgentRuntimeEnvironmentCache : IAgentRuntimeEnvironmentProvider
{
    public static readonly TimeSpan AvailableTtl = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan NotAvailableTtl = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRuntimeEnvironmentCache> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private Task<AgentRuntimeEnvironmentView>? _fetch;
    private DateTimeOffset _expiresAt;

    public AgentRuntimeEnvironmentCache(
        IServiceScopeFactory scopeFactory,
        ILogger<AgentRuntimeEnvironmentCache> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<AgentRuntimeEnvironmentView> GetAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            bool fresh = _fetch is not null
                && (!_fetch.IsCompleted || _timeProvider.GetUtcNow() < _expiresAt);
            if (!fresh)
            {
                _fetch = FetchAsync();
            }

            return _fetch!.WaitAsync(cancellationToken);
        }
    }

    // Never takes a caller's token: the fetch is shared, and one closed panel must not cancel
    // it for the others. The client's own timeout bounds it.
    private async Task<AgentRuntimeEnvironmentView> FetchAsync()
    {
        AgentRuntimeEnvironmentView view = await FetchViewAsync();

        lock (_gate)
        {
            _expiresAt = _timeProvider.GetUtcNow() + (view.IsAvailable ? AvailableTtl : NotAvailableTtl);
        }

        return view;
    }

    // The panel awaits this inside a UI event handler, so a throw here would break the editor
    // circuit; any failure reads as not available and is retried after NotAvailableTtl.
    private async Task<AgentRuntimeEnvironmentView> FetchViewAsync()
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            if (scope.ServiceProvider.GetService<IAgentRuntimeClient>() is not IAgentRuntimeClient client)
            {
                return AgentRuntimeEnvironmentView.NotAvailable;
            }

            ErrorOr<AgentRuntimeEnvironment> fetched = await client.GetEnvironmentAsync(CancellationToken.None);

            return fetched.IsError
                ? AgentRuntimeEnvironmentView.NotAvailable
                : new AgentRuntimeEnvironmentView(fetched.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fetching the agent runtime's environment text failed");
            return AgentRuntimeEnvironmentView.NotAvailable;
        }
    }
}
