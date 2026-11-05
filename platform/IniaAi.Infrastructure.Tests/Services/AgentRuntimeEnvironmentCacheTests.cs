using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRuntimeEnvironmentCacheTests
{
    private const string RUNTIME_VERSION = "0.3.1030";
    private const string ENVIRONMENT_TEXT = "## Your environment\nNo network.";

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    private readonly SettableClock _clock = new(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
    private readonly StubAgentRuntimeClient _runtime = new();

    private AgentRuntimeEnvironmentCache CacheWithClient()
    {
        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IAgentRuntimeClient>(_runtime)
            .BuildServiceProvider();

        return new AgentRuntimeEnvironmentCache(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeEnvironmentCache>.Instance,
            _clock);
    }

    [Fact]
    public async Task GetAsync_ServedText_IsFetchedOnceAndReusedWithinItsWindow()
    {
        _runtime.OnGetEnvironment = static () => new AgentRuntimeEnvironment(RUNTIME_VERSION, ENVIRONMENT_TEXT);
        AgentRuntimeEnvironmentCache cache = CacheWithClient();

        AgentRuntimeEnvironmentView first = await cache.GetAsync(TestContext.Current.CancellationToken);
        _clock.Now += AgentRuntimeEnvironmentCache.AvailableTtl - TimeSpan.FromSeconds(1);
        AgentRuntimeEnvironmentView second = await cache.GetAsync(TestContext.Current.CancellationToken);

        Assert.True(first.IsAvailable);
        Assert.Equal(ENVIRONMENT_TEXT, first.Environment!.Text);
        Assert.Same(first, second);
        Assert.Equal(1, _runtime.EnvironmentFetchCount);
    }

    [Fact]
    public async Task GetAsync_AfterTheWindow_FetchesAgain()
    {
        _runtime.OnGetEnvironment = static () => new AgentRuntimeEnvironment(RUNTIME_VERSION, ENVIRONMENT_TEXT);
        AgentRuntimeEnvironmentCache cache = CacheWithClient();

        _ = await cache.GetAsync(TestContext.Current.CancellationToken);
        _clock.Now += AgentRuntimeEnvironmentCache.AvailableTtl;
        _ = await cache.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, _runtime.EnvironmentFetchCount);
    }

    [Fact]
    public async Task GetAsync_OlderRuntime_IsNotAvailable_AndRetriedSooner()
    {
        _runtime.OnGetEnvironment = static () => Error.NotFound(AgentRuntimeEnvironmentErrors.NOT_SERVED, "absent");
        AgentRuntimeEnvironmentCache cache = CacheWithClient();

        AgentRuntimeEnvironmentView view = await cache.GetAsync(TestContext.Current.CancellationToken);
        _clock.Now += AgentRuntimeEnvironmentCache.NotAvailableTtl;
        _runtime.OnGetEnvironment = static () => new AgentRuntimeEnvironment(RUNTIME_VERSION, ENVIRONMENT_TEXT);
        AgentRuntimeEnvironmentView upgraded = await cache.GetAsync(TestContext.Current.CancellationToken);

        Assert.False(view.IsAvailable);
        Assert.True(upgraded.IsAvailable);
        Assert.Equal(2, _runtime.EnvironmentFetchCount);
    }

    [Fact]
    public async Task GetAsync_HostWithAgentsDisabled_IsNotAvailable()
    {
        ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var cache = new AgentRuntimeEnvironmentCache(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRuntimeEnvironmentCache>.Instance,
            _clock);

        AgentRuntimeEnvironmentView view = await cache.GetAsync(TestContext.Current.CancellationToken);

        Assert.False(view.IsAvailable);
    }

    [Fact]
    public async Task GetAsync_ClientThrows_IsNotAvailable_AndRetriedSooner()
    {
        _runtime.OnGetEnvironment = static () => throw new InvalidOperationException("bad base url");
        AgentRuntimeEnvironmentCache cache = CacheWithClient();

        AgentRuntimeEnvironmentView view = await cache.GetAsync(TestContext.Current.CancellationToken);
        _clock.Now += AgentRuntimeEnvironmentCache.NotAvailableTtl;
        _runtime.OnGetEnvironment = static () => new AgentRuntimeEnvironment(RUNTIME_VERSION, ENVIRONMENT_TEXT);
        AgentRuntimeEnvironmentView recovered = await cache.GetAsync(TestContext.Current.CancellationToken);

        Assert.False(view.IsAvailable);
        Assert.True(recovered.IsAvailable);
    }
}
