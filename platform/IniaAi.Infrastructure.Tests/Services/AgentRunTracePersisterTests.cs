using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Services;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunTracePersisterTests : IDisposable
{
    private const string ORG_ID = "org-persister";
    private const string BLOCK_ID = "blk-persister";
    private const string RUN_ID = "exec_blk_w_1";

    private readonly DbContextOptions<UserDataDbContext> _options;
    private readonly ServiceProvider _serviceProvider;
    private readonly AgentRunTracePersister _persister;

    public AgentRunTracePersisterTests()
    {
        _options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"AgentRunTracePersisterTests-{Guid.NewGuid()}")
            .Options;

        var services = new ServiceCollection();
        _ = services.AddScoped(_ => new UserDataDbContext(_options));
        _serviceProvider = services.BuildServiceProvider();

        _persister = new AgentRunTracePersister(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new DbExceptionAnalyzer(),
            NullLogger<AgentRunTracePersister>.Instance);
    }

    /// <summary>
    /// Treats every DbUpdateException as a unique violation, standing in for the provider-typed
    /// inner errors (PostgresException 23505 / SqlException 2601-2627) that cannot be
    /// constructed in a unit test.
    /// </summary>
    private sealed class EveryDbUpdateIsAViolationAnalyzer : IDbExceptionAnalyzer
    {
        public bool IsCounterpartyNameDuplicate(Exception exception)
        {
            return false;
        }

        public bool IsUniqueConstraintViolation(Exception exception)
        {
            return exception is DbUpdateException;
        }

        public bool IsConcurrencyConflict(Exception exception)
        {
            return false;
        }
    }

    private AgentRunTracePersister RacingPersister(
        IDbExceptionAnalyzer analyzer,
        Func<int, Task> onBeforeSave)
    {
        return new AgentRunTracePersister(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            analyzer,
            NullLogger<AgentRunTracePersister>.Instance,
            onBeforeSave);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SeedBlockAsync()
    {
        var workflow = new Workflow("persister-test") { Id = "wf-persister", OrganizationId = ORG_ID };
        var section = new Section("S", workflow) { Id = "sec-persister", Order = 0 };
        _ = new Block(BlockTypes.Agent, section, replacementTag: BLOCK_ID)
        {
            Id = BLOCK_ID,
            Name = BLOCK_ID,
        };

        using var context = new UserDataDbContext(_options);
        _ = context.Add(workflow);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static AgentRunTraceFile Row(string path, string? blockId = BLOCK_ID)
    {
        return new AgentRunTraceFile
        {
            OrganizationId = ORG_ID,
            BlockId = blockId,
            RunExecutionId = RUN_ID,
            Attempt = 1,
            Path = path,
            Sha256 = new string('0', 64),
            SizeBytes = 3,
            Content = [1, 2, 3],
        };
    }

    [Fact]
    public async Task PersistAsync_CommitsTheRowsDurably()
    {
        await SeedBlockAsync();

        bool persisted = await _persister.PersistAsync(
            [Row("trace/trace.ndjson"), Row("trace/model.ndjson")],
            TestContext.Current.CancellationToken);

        Assert.True(persisted);
        using var context = new UserDataDbContext(_options);
        List<AgentRunTraceFile> stored = await context.Set<AgentRunTraceFile>()
            .Where(file => file.RunExecutionId == RUN_ID)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, static file => Assert.Equal(ORG_ID, file.OrganizationId));
    }

    [Fact]
    public async Task PersistAsync_RowsAlreadyPresentUnderTheirNaturalKey_AreNotDuplicated()
    {
        await SeedBlockAsync();
        _ = await _persister.PersistAsync([Row("trace/trace.ndjson")], TestContext.Current.CancellationToken);

        bool persisted = await _persister.PersistAsync(
            [Row("trace/trace.ndjson"), Row("trace/model.ndjson")],
            TestContext.Current.CancellationToken);

        Assert.True(persisted);
        using var context = new UserDataDbContext(_options);
        List<string> storedPaths = await context.Set<AgentRunTraceFile>()
            .Where(file => file.RunExecutionId == RUN_ID)
            .Select(file => file.Path)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["trace/model.ndjson", "trace/trace.ndjson"], storedPaths.Order().ToArray());
    }

    [Fact]
    public async Task PersistAsync_FailingStore_ReturnsFalseInsteadOfThrowing()
    {
        var services = new ServiceCollection();
        _ = services.AddScoped<UserDataDbContext>(
            _ => throw new InvalidOperationException("store unavailable"));
        using ServiceProvider broken = services.BuildServiceProvider();
        var persister = new AgentRunTracePersister(
            broken.GetRequiredService<IServiceScopeFactory>(),
            new DbExceptionAnalyzer(),
            NullLogger<AgentRunTracePersister>.Instance);

        bool persisted = await persister.PersistAsync(
            [Row("trace/trace.ndjson")], TestContext.Current.CancellationToken);

        Assert.False(persisted);
    }

    [Fact]
    public async Task PersistAsync_LosingTheInsertRaceToAConcurrentPersist_ConvergesInsteadOfFailing()
    {
        await SeedBlockAsync();
        AgentRunTraceFile contested = Row("trace/trace.ndjson");
        AgentRunTracePersister persister = RacingPersister(
            new EveryDbUpdateIsAViolationAnalyzer(),
            async attempt =>
            {
                if (attempt > 1)
                {
                    return;
                }

                // The rival teardown commits the same pack (fresh row id, same natural key) in the
                // window between this persister's existence check and its save - on a real provider
                // the unique natural-key index then fails the save with DbUpdateException.
                using var rival = new UserDataDbContext(_options);
                _ = rival.Add(Row("trace/trace.ndjson"));
                _ = await rival.SaveChangesAsync();
                throw new DbUpdateException("unique natural-key index violation");
            });

        bool persisted = await persister.PersistAsync([contested], TestContext.Current.CancellationToken);

        Assert.True(persisted);
        using var context = new UserDataDbContext(_options);
        _ = Assert.Single(await context.Set<AgentRunTraceFile>()
            .Where(file => file.RunExecutionId == RUN_ID)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PersistAsync_RivalCommittedOnlyPartOfThePack_RetryInsertsTheRest()
    {
        await SeedBlockAsync();
        AgentRunTracePersister persister = RacingPersister(
            new EveryDbUpdateIsAViolationAnalyzer(),
            async attempt =>
            {
                if (attempt > 1)
                {
                    return;
                }

                using var rival = new UserDataDbContext(_options);
                _ = rival.Add(Row("trace/trace.ndjson"));
                _ = await rival.SaveChangesAsync();
                throw new DbUpdateException("unique natural-key index violation");
            });

        bool persisted = await persister.PersistAsync(
            [Row("trace/trace.ndjson"), Row("trace/model.ndjson")],
            TestContext.Current.CancellationToken);

        Assert.True(persisted);
        using var context = new UserDataDbContext(_options);
        List<string> storedPaths = await context.Set<AgentRunTraceFile>()
            .Where(file => file.RunExecutionId == RUN_ID)
            .Select(file => file.Path)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["trace/model.ndjson", "trace/trace.ndjson"], storedPaths.Order().ToArray());
    }

    [Fact]
    public async Task PersistAsync_LosingTheRaceTwice_WithholdsInsteadOfLooping()
    {
        await SeedBlockAsync();
        int attempts = 0;
        AgentRunTracePersister persister = RacingPersister(
            new EveryDbUpdateIsAViolationAnalyzer(),
            attempt =>
            {
                attempts = attempt;
                throw new DbUpdateException("unique natural-key index violation");
            });

        bool persisted = await persister.PersistAsync(
            [Row("trace/trace.ndjson")], TestContext.Current.CancellationToken);

        Assert.False(persisted);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task PersistAsync_NonViolationDbUpdateFailure_WithholdsWithoutARetry()
    {
        await SeedBlockAsync();
        int attempts = 0;
        AgentRunTracePersister persister = RacingPersister(
            new DbExceptionAnalyzer(),
            attempt =>
            {
                attempts = attempt;
                throw new DbUpdateException("foreign key violation, not a race");
            });

        bool persisted = await persister.PersistAsync(
            [Row("trace/trace.ndjson")], TestContext.Current.CancellationToken);

        Assert.False(persisted);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task PersistAsync_NoRows_IsAVacuousSuccess()
    {
        bool persisted = await _persister.PersistAsync([], TestContext.Current.CancellationToken);

        Assert.True(persisted);
    }
}
