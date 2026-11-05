using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Executions;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Jobs;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Services.Tests.Jobs;

/// <summary>
/// A process start writes its row on the first heartbeat and moves its last-seen time on each later
/// one. With host-loss recovery off, its default, nothing is written.
/// SQLite, because the beat is a bulk update.
/// </summary>
public sealed class HostBootHeartbeatTests : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly HostBootIdentity _hostBoot = new();
    private readonly SteppedClock _clock = new();

    public HostBootHeartbeatTests()
    {
        var services = new ServiceCollection();
        _ = services.AddSqliteTestDatabaseServices()
            .AddScoped<IUserDataDbContext>(provider => provider.GetRequiredService<UserDataDbContext>())
            .AddSingleton(_hostBoot)
            .AddSingleton<TimeProvider>(_clock)
            .AddSingleton<IHostEnvironment>(new HostingEnvironment { ApplicationName = "IniaAi.WebApi" })
            .AddScoped<HostBootHeartbeat>();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    [Fact]
    public async Task TheFirstBeat_WritesTheRow_AndEachLaterBeatMovesItsLastSeenTime()
    {
        await BeatAsync();
        DateTime started = _clock.GetUtcNow().UtcDateTime;
        _clock.Now += TimeSpan.FromSeconds(30);

        await BeatAsync();

        HostBoot boot = Assert.Single(await StoredBootsAsync());
        Assert.Equal(_hostBoot.Id, boot.Id);
        Assert.Equal("IniaAi.WebApi", boot.Application);
        Assert.Equal(Environment.MachineName, boot.Machine);
        Assert.Equal(started, boot.StartedAt);
        Assert.Equal(started.AddSeconds(30), boot.LastSeenAt);
        Assert.Equal(started, _hostBoot.HeartbeatHealthySince?.UtcDateTime);
    }

    [Fact]
    public async Task WithRecoveryOff_TheServiceWritesNothing()
    {
        using HostBootHeartbeatService service = Service(hostLossRecovery: false);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!;

        Assert.Empty(await StoredBootsAsync());
    }

    [Fact]
    public async Task WithRecoveryOn_TheServiceBeatsAtStart()
    {
        var logger = new CapturingLogger<HostBootHeartbeatService>();
        using HostBootHeartbeatService service = Service(hostLossRecovery: true, logger);

        await service.StartAsync(TestContext.Current.CancellationToken);
        // The beat runs on the thread pool, which a busy CI runner can be slow to give it. The beat
        // and this test also share one in-memory SQLite connection, which is not safe across
        // threads, so wait on the beat's own signal and read the table only after the service stops.
        await Eventually.UntilAsync(
            () => _hostBoot.HeartbeatHealthySince is not null || logger.Entries.Any(entry => entry.Level == LogLevel.Error),
            "the first heartbeat to succeed or fail",
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken: TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Null(logger.Entries.FirstOrDefault(entry => entry.Level == LogLevel.Error)?.Exception);
        Assert.Equal(_hostBoot.Id, Assert.Single(await StoredBootsAsync()).Id);
    }

    private HostBootHeartbeatService Service(bool hostLossRecovery, ILogger<HostBootHeartbeatService>? logger = null)
    {
        return new HostBootHeartbeatService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WorkflowExecutionJanitorSettings { HostLossRecovery = hostLossRecovery }),
            logger ?? NullLogger<HostBootHeartbeatService>.Instance);
    }

    private async Task BeatAsync()
    {
        using IServiceScope scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HostBootHeartbeat>().BeatAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<HostBoot>> StoredBootsAsync()
    {
        using IServiceScope scope = _provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<UserDataDbContext>()
            .Set<HostBoot>()
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}
