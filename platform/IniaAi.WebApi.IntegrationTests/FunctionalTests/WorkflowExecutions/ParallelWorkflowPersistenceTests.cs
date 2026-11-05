using IniaAi.Configuration;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.WorkflowExecutions;

/// <summary>
/// Change-tracker-race verification: proves the per-block persistence path is correct against a REAL
/// Postgres database at <c>MaxBlockParallelism &gt;= 2</c>. The in-memory EF provider used by the
/// unit suites tolerates the race the pooled-context writer removes, so the race can only be
/// reproduced (and the fix proven) against Npgsql with genuine concurrency.
/// <para>
/// Runs a multi-section / multi-block workflow through the real
/// <see cref="IWorkflowHandler.ExecuteWorkflowAsync"/> and asserts: (1) the run completes with no
/// <see cref="InvalidOperationException"/> / <c>DbUpdateException</c>; (2) every block's
/// <see cref="Block.ExecutionState"/> and <see cref="BlockResponse"/> is persisted; and (3) on a
/// mid-run failure, the blocks that completed before the failing block are still persisted.
/// </para>
/// <para>
/// CI-excluded (README.md, the integration tests): requires a live Postgres reachable via <c>appsettings.Tests.json</c>.
/// </para>
/// </summary>
public class ParallelWorkflowPersistenceTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string MERGE_FIRST_TEMPLATE = "first block, no upstream";
    private const string BOOM_TAG = "boom_block";

    private readonly ApiTestFixture _fixture;

    public ParallelWorkflowPersistenceTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ExecuteWorkflow_AtCapTwoOrMore_PersistsEveryBlockWithoutConcurrencyErrors()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        IServiceScope scope = _fixture.ClientBuilder.GetNewScope<IServiceScopeFactory>().Service.CreateScope();

        try
        {
            AssertParallelismIsAtLeastTwo(scope);

            IUserDataUnitOfWork userDataUnitOfWork = scope.ServiceProvider
                .GetRequiredService<IUserDataUnitOfWork>();
            IWorkflowHandler handler = ResolveGenericHandler(scope);

            Workflow workflow = BuildMultiSectionMergeWorkflow(
                handle.User.OrganizationId,
                handle.User.Id,
                failingTag: null);

            await handler.CreateRootOutputAsync(
                userDataUnitOfWork,
                Guid.NewGuid().ToString(),
                workflow,
                handle.User.OrganizationId,
                handle.User.Id);

            // The fix proves itself by NOT throwing: a torn jsonb write or a concurrent
            // change-tracker access surfaces as InvalidOperationException / DbUpdateException here.
            Workflow executed = await handler.ExecuteWorkflowAsync(
                workflow,
                anonymisationMap: new(),
                organizationId: handle.User.OrganizationId,
                jobId: Guid.NewGuid().ToString(),
                userDataUnitOfWork,
                userId: handle.User.Id,
                inputs: [],
                cancellationToken: cancellationToken);

            List<string> executedBlockIds = CollectBlockIds(executed);
            Assert.True(executedBlockIds.Count >= 6, "Expected a multi-block workflow.");

            await AssertEveryBlockPersistedAsync(scope, executedBlockIds, cancellationToken);
            await AssertExecutionStateCarriersPersistedAsync(scope, executed, cancellationToken);
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <summary>
    /// The shared tracker is cleared before execution, so workflow/section/text-input
    /// <c>ExecutionState</c> snapshots are only persisted by the explicit writers; this guards the
    /// audit-snapshot regression where they stayed NULL forever.
    /// </summary>
    private static async Task AssertExecutionStateCarriersPersistedAsync(
        IServiceScope scope,
        Workflow executed,
        CancellationToken cancellationToken)
    {
        await using UserDataDbContext db = await CreateReadContextAsync(scope);

        // No engine path sets a workflow-level ExecutionState today (matches develop), so only the
        // row's existence is asserted; sections and text inputs must carry persisted state.
        Workflow? persistedWorkflow = await db.Workflows
            .AsNoTracking()
            .SingleOrDefaultAsync(w => w.Id == executed.Id, cancellationToken);

        Assert.NotNull(persistedWorkflow);

        foreach (Section section in executed.Sections)
        {
            Section? persistedSection = await db.Sections
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == section.Id, cancellationToken);

            Assert.True(persistedSection is not null, $"Section {section.Id} was not persisted.");
            Assert.True(
                persistedSection!.ExecutionState is not null,
                $"Section {section.Id} persisted a NULL ExecutionState.");
        }

        foreach (TextInput textInput in CollectBlocks(executed).SelectMany(b => b.TextInputs))
        {
            TextInput? persistedInput = await db.TextInputs
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == textInput.Id, cancellationToken);

            Assert.True(persistedInput is not null, $"TextInput {textInput.Id} was not persisted.");
            Assert.True(
                persistedInput!.ExecutionState is not null,
                $"TextInput {textInput.Id} persisted a NULL ExecutionState.");
        }
    }

    [Fact]
    public async Task ExecuteWorkflow_WhenABlockFailsMidRun_PersistsBlocksThatCompletedBefore()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        IServiceScope scope = _fixture.ClientBuilder.GetNewScope<IServiceScopeFactory>().Service.CreateScope();

        try
        {
            AssertParallelismIsAtLeastTwo(scope);

            IUserDataUnitOfWork userDataUnitOfWork = scope.ServiceProvider
                .GetRequiredService<IUserDataUnitOfWork>();

            FailingBlockHandler handler = new(
                scope.ServiceProvider,
                throwForReplacementTag: BOOM_TAG);

            Workflow workflow = BuildMultiSectionMergeWorkflow(
                handle.User.OrganizationId,
                handle.User.Id,
                failingTag: BOOM_TAG);

            await handler.CreateRootOutputAsync(
                userDataUnitOfWork,
                Guid.NewGuid().ToString(),
                workflow,
                handle.User.OrganizationId,
                handle.User.Id);

            Workflow executed = await handler.ExecuteWorkflowAsync(
                workflow,
                anonymisationMap: new(),
                organizationId: handle.User.OrganizationId,
                jobId: Guid.NewGuid().ToString(),
                userDataUnitOfWork,
                userId: handle.User.Id,
                inputs: [],
                cancellationToken: cancellationToken);

            List<Block> nonFailingBlocks = [.. CollectBlocks(executed)
                .Where(b => b.ReplacementTag != BOOM_TAG)];

            Assert.NotEmpty(nonFailingBlocks);

            await using UserDataDbContext db = await CreateReadContextAsync(scope);

            foreach (Block block in nonFailingBlocks)
            {
                Block? persisted = await db.Blocks
                    .AsNoTracking()
                    .SingleOrDefaultAsync(b => b.Id == block.Id, cancellationToken);

                Assert.True(
                    persisted is not null,
                    $"Block {block.Id} ({block.ReplacementTag}) that completed before the failure was not persisted.");
                Assert.NotNull(persisted!.ExecutionState);
            }

            Block failingBlock = CollectBlocks(executed).Single(b => b.ReplacementTag == BOOM_TAG);
            Block? persistedFailing = await db.Blocks
                .AsNoTracking()
                .SingleOrDefaultAsync(b => b.Id == failingBlock.Id, cancellationToken);

            Assert.NotNull(persistedFailing);
            Assert.NotNull(persistedFailing!.ExecutionState);
            Assert.Equal(ExecutionStatus.Failed, persistedFailing.ExecutionState!.Status);
            Assert.NotNull(persistedFailing.ExecutionState.Error);
        }
        finally
        {
            scope.Dispose();
        }
    }

    private static void AssertParallelismIsAtLeastTwo(IServiceScope scope)
    {
        PlatformSettings settings = scope.ServiceProvider
            .GetRequiredService<IOptions<PlatformSettings>>()
            .Value;

        Assert.True(
            settings.MaxBlockParallelism >= 2,
            $"This test must run at MaxBlockParallelism >= 2 to exercise the race; configured value was {settings.MaxBlockParallelism}.");
    }

    private static IWorkflowHandler ResolveGenericHandler(IServiceScope scope)
    {
        return scope.ServiceProvider.GetRequiredKeyedService<IWorkflowHandler>(WorkflowType.GENERIC);
    }

    private static async Task<UserDataDbContext> CreateReadContextAsync(IServiceScope scope)
    {
        IDbContextFactory<UserDataDbContext> factory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();

        return await factory.CreateDbContextAsync();
    }

    private static async Task AssertEveryBlockPersistedAsync(
        IServiceScope scope,
        IReadOnlyList<string> blockIds,
        CancellationToken cancellationToken)
    {
        await using UserDataDbContext db = await CreateReadContextAsync(scope);

        foreach (string blockId in blockIds)
        {
            Block? persisted = await db.Blocks
                .AsNoTracking()
                .SingleOrDefaultAsync(b => b.Id == blockId, cancellationToken);

            Assert.True(persisted is not null, $"Block {blockId} was not persisted.");
            Assert.NotNull(persisted!.ExecutionState);

            BlockResponse? response = await db.BlockResponses
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.BlockId == blockId, cancellationToken);

            Assert.True(
                response is not null,
                $"BlockResponse for block {blockId} was not persisted.");
            Assert.False(
                string.IsNullOrEmpty(response!.RawResponse),
                $"BlockResponse for block {blockId} persisted an empty RawResponse.");
        }
    }

    /// <summary>
    /// Root workflow with three sections of two <see cref="BlockTypes.Merge"/> blocks each (six
    /// blocks). Merge blocks are pure, so the run is deterministic; the second block in each section
    /// reads the first via its placeholder so the DAG has real edges. When <paramref name="failingTag"/>
    /// is supplied the section-1 second block carries it so the test handler can target it.
    /// </summary>
    private static Workflow BuildMultiSectionMergeWorkflow(
        string organizationId,
        string userId,
        string? failingTag)
    {
        Workflow root = new()
        {
            Id = Guid.NewGuid().ToString(),
            Title = "parallel-persistence-root",
            OrganizationId = organizationId,
            UserId = userId,
        };

        for (int sectionIndex = 0; sectionIndex < 3; sectionIndex++)
        {
            Section section = new($"section-{sectionIndex}", root)
            {
                Id = Guid.NewGuid().ToString(),
                Title = $"section-{sectionIndex}",
                Order = sectionIndex,
            };

            string firstTag = $"sec{sectionIndex}_first";
            string secondTag = sectionIndex == 1 && failingTag is not null
                ? failingTag
                : $"sec{sectionIndex}_second";

            Block first = new(BlockTypes.Merge, section, replacementTag: firstTag)
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"Section {sectionIndex} First",
                Properties = new MergeProperties
                {
                    TextTemplate = MERGE_FIRST_TEMPLATE,
                },
            };

            _ = new TextInput(first, $"sec{sectionIndex}_ti", "static text input value");

            _ = new Block(BlockTypes.Merge, section, replacementTag: secondTag)
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"Section {sectionIndex} Second",
                Properties = new MergeProperties
                {
                    TextTemplate = $"second reads from {{{firstTag}}}",
                },
            };
        }

        return root;
    }

    private static List<string> CollectBlockIds(Workflow root)
    {
        return [.. CollectBlocks(root).Select(b => b.Id)];
    }

    private static List<Block> CollectBlocks(Workflow root)
    {
        List<Block> blocks = [];

        foreach (Section section in root.Sections.OrderBy(s => s.Order))
        {
            blocks.AddRange(section.Blocks.OrderBy(b => b.Order));
        }

        return blocks;
    }

    /// <summary>
    /// Test-only handler that throws once the designated block has executed its strategy, simulating
    /// a mid-run failure for that single block. Continue-on-failure persists the failed block and
    /// keeps running the rest.
    /// </summary>
    private sealed class FailingBlockHandler : IniaAi.Core.Workflows.WorkflowHandler
    {
        private readonly string _throwForReplacementTag;

        public FailingBlockHandler(IServiceProvider serviceProvider, string throwForReplacementTag)
            : base(
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<IniaAi.Core.Workflows.WorkflowHandler>>(),
                serviceProvider.GetRequiredService<IOptions<PlatformSettings>>(),
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<IniaAi.Core.Workflows.Outputs.Interfaces.IWorkflowOutputService>(),
                serviceProvider.GetRequiredService<IniaAi.Common.Interfaces.IDocumentProcessingService>(),
                serviceProvider.GetRequiredService<IniaAi.Common.Interfaces.Persistence.IFileStorageManager>(),
                serviceProvider.GetRequiredService<IniaAi.Core.Services.Abstraction.IEntityPlaceholderResolver>(),
                serviceProvider.GetRequiredService<IniaAi.Core.Workflows.Graph.IWorkflowGraphBuilder>(),
                serviceProvider.GetRequiredService<IniaAi.Core.Workflows.Scheduling.LevelScheduler>(),
                serviceProvider.GetRequiredService<IniaAi.Core.Workflows.Scheduling.IExecutedBlockWriter>())
        {
            _throwForReplacementTag = throwForReplacementTag;
        }

        protected override Task OnBlockExecutedAsync(
            Block block,
            IUserDataUnitOfWork userDataUnitOfWork,
            CancellationToken cancellationToken)
        {
            if (string.Equals(block.ReplacementTag, _throwForReplacementTag, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Simulated mid-run failure for block {block.Id} ({block.ReplacementTag}).");
            }

            return Task.CompletedTask;
        }
    }
}
