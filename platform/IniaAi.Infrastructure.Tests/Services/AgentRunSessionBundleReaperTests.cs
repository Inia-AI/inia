using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Retention against a real UserData context and the house in-memory storage service: every
/// "the bytes are gone" here is a read that came back empty, not a call that was observed.
/// </summary>
public class AgentRunSessionBundleReaperTests : IDisposable
{
    private const string ORG_ID = "org-reaper";
    private const string EXECUTION_ID = "exec-reaper";
    private const string OTHER_EXECUTION_ID = "exec-elsewhere";
    private const string MEMBER_A = "blk-member-a";
    private const string MEMBER_B = "blk-member-b";

    private readonly DbContextOptions<UserDataDbContext> _options;
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly TestFileStorageService _storageService = new();
    private readonly AgentRunSessionBundleReaper _reaper;

    public AgentRunSessionBundleReaperTests()
    {
        _options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"AgentRunSessionBundleReaperTests-{Guid.NewGuid()}")
            .Options;

        var services = new ServiceCollection();
        _ = services.AddScoped(_ => new UserDataDbContext(_options));
        _serviceProvider = services.BuildServiceProvider();

        _reaper = new AgentRunSessionBundleReaper(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new FileStorageManager(
                new TestFileStorageResolver(_storageService),
                Options.Create(new FileStorageSettings { DefaultProvider = "Database" })),
            NullLogger<AgentRunSessionBundleReaper>.Instance);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _serviceProvider.Dispose();
    }

    private async Task SeedExecutionAsync(string executionId, params string[] blockIds)
    {
        using var context = new UserDataDbContext(_options);
        var section = new Section { Id = $"sec-{executionId}", WorkflowId = executionId };
        _ = context.Add(section);

        foreach (string blockId in blockIds)
        {
            var block = new Block(BlockTypes.Agent, section) { Id = blockId };
            _ = context.Add(block);
        }

        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<AgentRunSessionBundle> HoldAsync(
        string blockId,
        int round,
        DateTime? createdAt = null)
    {
        string storageKey = AgentRunSessionBundleStorage.NewStorageKey();
        _storageService.AddTestFile(
            ORG_ID,
            StorageCategories.AgentSessionBundles,
            storageKey,
            Encoding.UTF8.GetBytes($"{blockId}/{round}"));

        var bundle = new AgentRunSessionBundle
        {
            OrganizationId = ORG_ID,
            BlockId = blockId,
            Round = round,
            RunExecutionId = $"run-{blockId}-{round}",
            Attempt = 1,
            Segment = AgentRunCheckpoint.TERMINAL_SEGMENT,
            Sha256 = new string('b', 64),
            SizeBytes = 8,
            StorageProvider = StorageProvider.Database.ToString(),
            StorageKey = storageKey,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };

        using var context = new UserDataDbContext(_options);
        _ = context.Add(bundle);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return bundle;
    }

    private async Task<List<AgentRunSessionBundle>> RowsAsync()
    {
        using var context = new UserDataDbContext(_options);

        return await context.Set<AgentRunSessionBundle>()
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private bool BytesHeld(AgentRunSessionBundle bundle)
    {
        return _storageService.GetTestFile(
            ORG_ID, StorageCategories.AgentSessionBundles, bundle.StorageKey) is not null;
    }

    [Fact]
    public async Task ReleaseExecutionAsync_Everything_TakesEveryRowAndItsBytes()
    {
        await SeedExecutionAsync(EXECUTION_ID, MEMBER_A, MEMBER_B);
        AgentRunSessionBundle first = await HoldAsync(MEMBER_A, round: 1);
        AgentRunSessionBundle second = await HoldAsync(MEMBER_A, round: 2);
        AgentRunSessionBundle other = await HoldAsync(MEMBER_B, round: 1);

        await _reaper.ReleaseExecutionAsync(EXECUTION_ID, ORG_ID, AgentRunSessionBundleRelease.Everything);

        Assert.Empty(await RowsAsync());
        Assert.False(BytesHeld(first));
        Assert.False(BytesHeld(second));
        Assert.False(BytesHeld(other));
    }

    /// <summary>
    /// A failed execution keeps what each member had last reached, and nothing behind it: one
    /// bundle per block, which is what caps a failed group at one per member plus the verifier.
    /// </summary>
    [Fact]
    public async Task ReleaseExecutionAsync_ExceptTheLastLeg_KeepsOneBundlePerBlock()
    {
        await SeedExecutionAsync(EXECUTION_ID, MEMBER_A, MEMBER_B);
        AgentRunSessionBundle firstRound = await HoldAsync(MEMBER_A, round: 1);
        AgentRunSessionBundle lastRound = await HoldAsync(MEMBER_A, round: 3);
        AgentRunSessionBundle middleRound = await HoldAsync(MEMBER_A, round: 2);
        AgentRunSessionBundle onlyRound = await HoldAsync(MEMBER_B, round: 1);

        await _reaper.ReleaseExecutionAsync(
            EXECUTION_ID, ORG_ID, AgentRunSessionBundleRelease.ExceptTheLastLegOfEachBlock);

        List<string> kept = [.. (await RowsAsync()).Select(static row => row.Id).Order(StringComparer.Ordinal)];
        Assert.Equal([.. new[] { lastRound.Id, onlyRound.Id }.Order(StringComparer.Ordinal)], kept);
        Assert.True(BytesHeld(lastRound));
        Assert.True(BytesHeld(onlyRound));
        Assert.False(BytesHeld(firstRound));
        Assert.False(BytesHeld(middleRound));
    }

    [Fact]
    public async Task ReleaseExecutionAsync_AnotherExecutionsBundles_AreLeftAlone()
    {
        await SeedExecutionAsync(EXECUTION_ID, MEMBER_A);
        await SeedExecutionAsync(OTHER_EXECUTION_ID, MEMBER_B);
        _ = await HoldAsync(MEMBER_A, round: 1);
        AgentRunSessionBundle elsewhere = await HoldAsync(MEMBER_B, round: 1);

        await _reaper.ReleaseExecutionAsync(EXECUTION_ID, ORG_ID, AgentRunSessionBundleRelease.Everything);

        AgentRunSessionBundle surviving = Assert.Single(await RowsAsync());
        Assert.Equal(elsewhere.Id, surviving.Id);
        Assert.True(BytesHeld(elsewhere));
    }

    [Fact]
    public async Task ReleaseExecutionAsync_AnotherOrganizationsBundles_AreLeftAlone()
    {
        await SeedExecutionAsync(EXECUTION_ID, MEMBER_A);
        AgentRunSessionBundle held = await HoldAsync(MEMBER_A, round: 1);

        await _reaper.ReleaseExecutionAsync(
            EXECUTION_ID, "org-someone-else", AgentRunSessionBundleRelease.Everything);

        _ = Assert.Single(await RowsAsync());
        Assert.True(BytesHeld(held));
    }

    [Fact]
    public async Task SweepExpiredAsync_TakesRowsPastTheWindowAndLeavesTheRest()
    {
        await SeedExecutionAsync(EXECUTION_ID, MEMBER_A, MEMBER_B);
        AgentRunSessionBundle aged = await HoldAsync(
            MEMBER_A, round: 1, createdAt: DateTime.UtcNow.AddDays(-8));
        AgentRunSessionBundle fresh = await HoldAsync(
            MEMBER_B, round: 1, createdAt: DateTime.UtcNow.AddDays(-1));

        int reaped = await _reaper.SweepExpiredAsync(
            DateTime.UtcNow.AddDays(-7), TestContext.Current.CancellationToken);

        Assert.Equal(1, reaped);
        AgentRunSessionBundle surviving = Assert.Single(await RowsAsync());
        Assert.Equal(fresh.Id, surviving.Id);
        Assert.False(BytesHeld(aged));
        Assert.True(BytesHeld(fresh));
    }

    [Fact]
    public async Task SweepExpiredAsync_NothingPastTheWindow_ReportsNothingReaped()
    {
        await SeedExecutionAsync(EXECUTION_ID, MEMBER_A);
        _ = await HoldAsync(MEMBER_A, round: 1);

        int reaped = await _reaper.SweepExpiredAsync(
            DateTime.UtcNow.AddDays(-7), TestContext.Current.CancellationToken);

        Assert.Equal(0, reaped);
        _ = Assert.Single(await RowsAsync());
    }

    /// <summary>
    /// A blob that cannot be deleted must not hold the sweep hostage: the row still goes, and so
    /// does every row behind it. The bytes are stranded, which the log names and the storage
    /// bill eventually shows — a sweep that stopped here would strand everything instead.
    /// </summary>
    [Fact]
    public async Task SweepExpiredAsync_StorageCannotDelete_StillTakesEveryRow()
    {
        await SeedExecutionAsync(EXECUTION_ID, MEMBER_A, MEMBER_B);
        _ = await HoldAsync(MEMBER_A, round: 1, createdAt: DateTime.UtcNow.AddDays(-8));
        _ = await HoldAsync(MEMBER_B, round: 1, createdAt: DateTime.UtcNow.AddDays(-8));

        var reaper = new AgentRunSessionBundleReaper(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new FileStorageManager(
                new FixedFileStorageResolver(new ThrowingFileStorageService()),
                Options.Create(new FileStorageSettings { DefaultProvider = "Database" })),
            NullLogger<AgentRunSessionBundleReaper>.Instance);

        int reaped = await reaper.SweepExpiredAsync(
            DateTime.UtcNow.AddDays(-7), TestContext.Current.CancellationToken);

        Assert.Equal(2, reaped);
        Assert.Empty(await RowsAsync());
    }
}
