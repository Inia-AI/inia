using System.Data.Common;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using IniaAi.Testing.DependencyInjection;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Runs <see cref="SoftDeleteCleanupService.CleanupAsync"/> on a real relational provider with a
/// retrying execution strategy and injected transient faults. A transient failure inside the
/// cleanup transaction must escape to the execution strategy so the whole transaction replays;
/// only a terminal failure (retries exhausted) may surface as a reported error.
/// </summary>
public class SoftDeleteCleanupServiceRetryTests
{
    private const string ORG = "org-retry";
    private const int RETENTION_DAYS = 7;

    [Fact]
    public async Task TransientFaultMidTransaction_RetryCommitsCleanupWithoutErrors()
    {
        using var harness = new SqliteCleanupHarness();
        string workflowId = await harness.SeedSoftDeletedWorkflowAsync(TestContext.Current.CancellationToken);

        harness.Interceptor.ArmAtNonQueryNumber(3);

        CleanupResult result = await harness.Service.CleanupAsync(
            ORG,
            RETENTION_DAYS,
            TestContext.Current.CancellationToken);

        Assert.True(harness.Interceptor.HasFired);
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.TotalDeleted);

        int remaining = await harness.Context.Workflows
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(w => w.Id == workflowId, TestContext.Current.CancellationToken);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task PersistentFaults_ExhaustRetriesAndReportSingleError()
    {
        using var harness = new SqliteCleanupHarness();
        string workflowId = await harness.SeedSoftDeletedWorkflowAsync(TestContext.Current.CancellationToken);

        harness.Interceptor.ArmEveryNonQuery();

        CleanupResult result = await harness.Service.CleanupAsync(
            ORG,
            RETENTION_DAYS,
            TestContext.Current.CancellationToken);

        Assert.Equal(1 + RetryOnTimeoutExecutionStrategy.MAX_RETRIES, harness.Interceptor.FireCount);
        _ = Assert.Single(result.Errors);
        Assert.Equal(0, result.TotalDeleted);

        int remaining = await harness.Context.Workflows
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(w => w.Id == workflowId, TestContext.Current.CancellationToken);
        Assert.Equal(1, remaining);
    }

    private sealed class SqliteCleanupHarness : IDisposable
    {
        public SqliteUserDataDbContext Context { get; }
        public SoftDeleteCleanupService Service { get; }
        public ThrowOnNthNonQueryInterceptor Interceptor { get; } = new();

        private readonly SqliteConnection _connection;

        public SqliteCleanupHarness()
        {
            _connection = SqliteUserDataSchema.OpenInMemoryDatabase();

            DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
                .UseSqlite(_connection, b => b.ExecutionStrategy(deps => new RetryOnTimeoutExecutionStrategy(deps)))
                .AddInterceptors(Interceptor)
                .Options;
            Context = new SqliteUserDataDbContext(options);

            Service = new SoftDeleteCleanupService(
                Context,
                new NoopFileStorageManager(),
                [],
                NullLogger<SoftDeleteCleanupService>.Instance);
        }

        public async Task<string> SeedSoftDeletedWorkflowAsync(CancellationToken ct)
        {
            _ = Context.Organizations.Add(new Organization("Cleanup Test Org", ORG));

            var workflow = new Workflow
            {
                Id = Guid.NewGuid().ToString(),
                Name = "stale",
                OrganizationId = ORG,
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow.AddDays(-(RETENTION_DAYS + 1)),
            };

            _ = Context.Workflows.Add(workflow);
            _ = await Context.SaveChangesAsync(ct);
            Context.ChangeTracker.Clear();

            return workflow.Id;
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }

    private sealed class RetryOnTimeoutExecutionStrategy : ExecutionStrategy
    {
        public const int MAX_RETRIES = 3;

        public RetryOnTimeoutExecutionStrategy(ExecutionStrategyDependencies dependencies)
            : base(dependencies, MAX_RETRIES, TimeSpan.Zero)
        {
        }

        protected override bool ShouldRetryOn(Exception exception)
        {
            return exception is TimeoutException;
        }
    }

    private sealed class ThrowOnNthNonQueryInterceptor : DbCommandInterceptor
    {
        private int _armedAtNonQueryNumber;
        private bool _throwOnEveryNonQuery;
        private int _nonQueryNumber;

        public int FireCount { get; private set; }
        public bool HasFired => FireCount > 0;

        public void ArmAtNonQueryNumber(int nonQueryNumber)
        {
            _armedAtNonQueryNumber = nonQueryNumber;
            _throwOnEveryNonQuery = false;
            _nonQueryNumber = 0;
            FireCount = 0;
        }

        public void ArmEveryNonQuery()
        {
            _armedAtNonQueryNumber = 0;
            _throwOnEveryNonQuery = true;
            _nonQueryNumber = 0;
            FireCount = 0;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed();
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            ThrowIfArmed();
            return base.NonQueryExecuting(command, eventData, result);
        }

        private void ThrowIfArmed()
        {
            _nonQueryNumber++;

            if (_throwOnEveryNonQuery)
            {
                FireCount++;
                throw new TimeoutException("Injected persistent transient failure");
            }

            if (_armedAtNonQueryNumber > 0 && _nonQueryNumber == _armedAtNonQueryNumber && !HasFired)
            {
                FireCount++;
                throw new TimeoutException("Injected transient failure");
            }
        }
    }
}
