using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using SqliteCoreDatabase = IniaAi.Testing.DependencyInjection.SqliteCoreDatabase;
using SqliteCoreDbContext = IniaAi.Testing.DependencyInjection.SqliteCoreDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using IniaAi.Testing.DependencyInjection;

namespace IniaAi.Application.UnitTests.Commands.Workflows.SaveWorkflow;

/// <summary>
/// Reproduces the retry replay of <see cref="SaveWorkflowCommandHandler"/> on a real relational
/// provider: attempt one fails mid-save with a transient error, the transaction rolls back, and
/// the execution strategy re-runs the whole delegate on the same scoped context. The retry must
/// persist a complete workflow graph and exactly one new version row even though the change
/// tracker still holds state from the failed attempt.
/// </summary>
public class SaveWorkflowCommandHandlerRetryTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";

    [Fact]
    public async Task TransientFailureAtVersionInsert_RetryPersistsCompleteGraphAndSingleNewVersion()
    {
        using var harness = new SqliteRetryHarness();
        _ = harness.Context.Set<WorkflowType>().Add(new WorkflowType(TYPE, "Report"));
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var pipeline = new WorkflowSavePipeline(
            harness.UnitOfWork,
            harness.Context,
            new WorkflowCanonicalizer(),
            new WorkflowDiffService(),
            new WorkflowNarrativeGenerator(),
            Options.Create(new AgentRuntimeSettings()),
            NullLogger<WorkflowSavePipeline>.Instance, SuperAdminLookups.OverEmptyUserData());
        var handler = new SaveWorkflowCommandHandler(
            pipeline,
            harness.Context,
            NullLogger<SaveWorkflowCommandHandler>.Instance);

        ErrorOr<SaveWorkflowResult> seed = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow("v1 prompt")),
            TestContext.Current.CancellationToken);
        Assert.False(seed.IsError);

        harness.Interceptor.ArmAtSaveNumber(3);

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow("v2 prompt")),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(harness.Interceptor.HasFired);

        int versionCount = await harness.Context.WorkflowVersions
            .CountAsync(v => v.WorkflowTypeId == TYPE, TestContext.Current.CancellationToken);
        int workflowCount = await harness.Context.Set<Workflow>()
            .CountAsync(TestContext.Current.CancellationToken);
        int sectionCount = await harness.Context.Set<Section>()
            .CountAsync(TestContext.Current.CancellationToken);
        int blockCount = await harness.Context.Set<Block>()
            .CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, versionCount);
        Assert.Equal(1, workflowCount);
        Assert.Equal(1, sectionCount);
        Assert.Equal(1, blockCount);
    }

    private static Workflow BuildWorkflow(string prompt)
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };

        return workflow;
    }

    private sealed class SqliteRetryHarness : IDisposable
    {
        private readonly SqliteCoreDatabase _database;

        public SqliteRetryHarness()
        {
            _database = new SqliteCoreDatabase(options => new FaultableCoreDbContext(options), Interceptor);
        }

        public SqliteCoreDbContext Context => _database.Context;
        public ICoreUnitOfWork UnitOfWork => _database.UnitOfWork;
        public ThrowOnNthSaveInterceptor Interceptor { get; } = new();

        public void Dispose()
        {
            _database.Dispose();
        }
    }

    private sealed class FaultableCoreDbContext : SqliteCoreDbContext
    {
        public FaultableCoreDbContext(DbContextOptions<CoreDbContext> options) : base(options)
        {
        }

        public override IExecutionStrategy CreateExecutionStrategy()
        {
            return new RetryOnTimeoutExecutionStrategy();
        }
    }

    private sealed class ThrowOnNthSaveInterceptor : SaveChangesInterceptor
    {
        private int _armedAtSaveNumber;
        private int _saveNumber;

        public bool HasFired { get; private set; }

        public void ArmAtSaveNumber(int saveNumber)
        {
            _armedAtSaveNumber = saveNumber;
            _saveNumber = 0;
            HasFired = false;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            ThrowIfArmed();
            return base.SavingChanges(eventData, result);
        }

        private void ThrowIfArmed()
        {
            _saveNumber++;
            if (_armedAtSaveNumber > 0 && _saveNumber == _armedAtSaveNumber && !HasFired)
            {
                HasFired = true;
                throw new TimeoutException("Injected transient failure");
            }
        }
    }

    private sealed class RetryOnTimeoutExecutionStrategy : IExecutionStrategy
    {
        private const int MAX_ATTEMPTS = 3;

        public bool RetriesOnFailure => true;

        public TResult Execute<TState, TResult>(
            TState state,
            Func<DbContext, TState, TResult> operation,
            Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return operation(null!, state);
                }
                catch (TimeoutException) when (attempt < MAX_ATTEMPTS)
                {
                }
            }
        }

        public async Task<TResult> ExecuteAsync<TState, TResult>(
            TState state,
            Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
            Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
            CancellationToken cancellationToken = default)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await operation(null!, state, cancellationToken);
                }
                catch (TimeoutException) when (attempt < MAX_ATTEMPTS)
                {
                }
            }
        }
    }
}
