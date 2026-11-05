using IniaAi.Core.Workflows;
using IniaAi.Domain.Constants;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// DOP=1 regression guard: when a section is skipped (here via the AF11 stop term in its execution
/// input), every block must persist <see cref="ExecutionStatus.Skipped"/> to the UserData store.
/// Under the parallel wire-in the executed graph is detached after the skeleton insert, so the skip
/// path has to route each block through the per-block writer; without that the row keeps its
/// skeleton status. Runs in-memory (the shared store is visible to a fresh read context).
/// </summary>
public class SectionSkipPersistenceTests : IClassFixture<WorkflowsFixture<SectionSkipPersistenceTests>>
{
    private const string ORG_ID = "TestOrg";
    private readonly IServiceProvider _sp;

    public SectionSkipPersistenceTests(
        WorkflowsFixture<SectionSkipPersistenceTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task SkippedSection_PersistsEveryBlockAsSkipped()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        WorkflowHandler handler = scope.ServiceProvider.GetRequiredService<WorkflowHandler>();

        Workflow workflow = BuildSkippedSectionWorkflow();
        string jobId = Guid.NewGuid().ToString();

        // Create the execution snapshot so RootOutput is set; otherwise the handler runs in editor /
        // dev mode and persists nothing.
        await handler.CreateRootOutputAsync(uow, jobId, workflow, ORG_ID, userId: null);

        Workflow executed = await handler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: new(),
            organizationId: ORG_ID,
            jobId: jobId,
            uow,
            inputs: [],
            cancellationToken: ct);

        List<Block> executedBlocks = [.. executed.Sections.SelectMany(s => s.Blocks)];
        Assert.NotEmpty(executedBlocks);

        await using UserDataDbContext db = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>()
            .CreateDbContextAsync(ct);

        foreach (Block block in executedBlocks)
        {
            Block? persisted = await db.Blocks
                .AsNoTracking()
                .SingleOrDefaultAsync(b => b.Id == block.Id, ct);

            Assert.True(persisted is not null, $"Block {block.Id} was not persisted.");
            Assert.NotNull(persisted!.ExecutionState);
            Assert.Equal(ExecutionStatus.Skipped, persisted.ExecutionState!.Status);
        }
    }

    [Fact]
    public async Task SkippedSection_WritesAPerBlockLogRow()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        WorkflowHandler handler = scope.ServiceProvider.GetRequiredService<WorkflowHandler>();

        Workflow workflow = BuildSkippedSectionWorkflow();
        string jobId = Guid.NewGuid().ToString();

        await handler.CreateRootOutputAsync(uow, jobId, workflow, ORG_ID, userId: null);

        Workflow executed = await handler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: new(),
            organizationId: ORG_ID,
            jobId: jobId,
            uow,
            inputs: [],
            cancellationToken: ct);

        List<Block> executedBlocks = [.. executed.Sections.SelectMany(s => s.Blocks)];
        Assert.NotEmpty(executedBlocks);

        await using UserDataDbContext db = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>()
            .CreateDbContextAsync(ct);

        foreach (Block block in executedBlocks)
        {
            WorkflowExecutionLog? row = await db.WorkflowExecutionLogs
                .AsNoTracking()
                .SingleOrDefaultAsync(log => log.BlockId == block.Id, ct);

            Assert.True(row is not null, $"Block {block.Id} left no execution log row.");
            Assert.Equal(BlockExecutionStatus.Skipped, row!.Status);
        }
    }

    private static Workflow BuildSkippedSectionWorkflow()
    {
        Workflow root = new() { Id = "skip-root", Title = "skip-root" };

        Section section = new("skip-section", root)
        {
            Id = "skip-section",
            Order = 0,
            TextInputTemplate = StopMarker.VALUE,
        };

        for (int i = 0; i < 2; i++)
        {
            _ = new Block(BlockTypes.Merge, section, replacementTag: $"skip_block_{i}")
            {
                Id = $"skip-blk-{i}",
                Name = $"Skip Block {i}",
                Properties = new MergeProperties { TextTemplate = $"block {i} body" },
            };
        }

        return root;
    }
}
