using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunLimitsRefreshServiceTests
{
    private static readonly AgentRunLimits _limits = new(
        new AgentRunFuseLimit(900_000m, 900_000m, "milliseconds"),
        new AgentRunFuseLimit(1.25m, 2.5m, "usd"),
        new AgentRunFuseLimit(300m, 300m, "turns"),
        new AgentRunFuseLimit(134_217_728m, 276_824_064m, "bytes"));

    private sealed class FixedLimitsClient : IAgentRuntimeClient
    {
        private readonly ErrorOr<AgentRunLimits> _fetchResult;

        public FixedLimitsClient(ErrorOr<AgentRunLimits> fetchResult)
        {
            _fetchResult = fetchResult;
        }

        public Task<ErrorOr<AgentRunLimits>> GetLimitsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_fetchResult);
        }

        public Task<ErrorOr<AgentRuntimeEnvironment>> GetEnvironmentAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ErrorOr<AgentRunMonitor>> StartRunAsync(
            string executionId, string prompt, string runToken, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ErrorOr<AgentRunMonitor>> StartRunAsync(
            AgentRunStartRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ErrorOr<AgentRunMonitor>> GetRunAsync(
            string executionId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ErrorOr<AgentRunMonitor>> CancelRunAsync(
            string executionId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ErrorOr<AgentRunMonitor>> SubmitInputAsync(
            string executionId,
            string resumeToken,
            string questionId,
            string answer,
            bool allowFurtherPause = true,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ErrorOr<IReadOnlyList<AgentRunFileEntry>>> ListRunFilesAsync(
            string executionId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ErrorOr<byte[]>> GetRunFileAsync(
            string executionId, string path, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ErrorOr<AgentRunCheckpoint>> GetRunCheckpointAsync(
            string executionId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ErrorOr<AgentRunMonitor>> CompleteRunAsync(
            string executionId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private static (AgentRunLimitsRefreshService Service, AgentRunLimitsCache Cache) Create(
        ErrorOr<AgentRunLimits> fetchResult)
    {
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IAgentRuntimeClient>(_ => new FixedLimitsClient(fetchResult))
            .BuildServiceProvider();

        var cache = new AgentRunLimitsCache();
        var service = new AgentRunLimitsRefreshService(
            cache,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunLimitsRefreshService>.Instance);

        return (service, cache);
    }

    [Fact]
    public async Task ASuccessfulFetch_FillsTheCache()
    {
        (AgentRunLimitsRefreshService service, AgentRunLimitsCache cache) = Create(_limits);

        bool refreshed = await service.RefreshOnceAsync(TestContext.Current.CancellationToken);

        Assert.True(refreshed);
        Assert.Equal(_limits, cache.Enforceable);
    }

    [Fact]
    public async Task AFailedFetch_LeavesTheCacheUntouched()
    {
        (AgentRunLimitsRefreshService service, AgentRunLimitsCache cache) = Create(
            Error.Failure("AgentRun.Unreachable", "connection refused"));

        bool refreshed = await service.RefreshOnceAsync(TestContext.Current.CancellationToken);

        Assert.False(refreshed);
        Assert.Null(cache.LastKnown);
        Assert.Null(cache.Enforceable);
    }
}
