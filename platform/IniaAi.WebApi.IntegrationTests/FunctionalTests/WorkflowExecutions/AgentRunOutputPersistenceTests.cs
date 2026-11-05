using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.WorkflowExecutions;

/// <summary>
/// The append-only proof for the per-attempt durability home against real Postgres: a redo
/// leg's persist leaves the prior round's output row and trace rows intact, the settled
/// <see cref="BlockResponse"/> stays one row per block reflecting the selected attempt, and a
/// crash-replayed re-persist of the same round updates its row in place instead of violating
/// the natural key. CI-excluded (README.md, the integration tests): requires a live Postgres reachable via
/// <c>appsettings.Tests.json</c>.
/// </summary>
public class AgentRunOutputPersistenceTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string ORGANIZATION_ID = "org-attempt-rows";

    // Trace rows dedupe on the global natural key (RunExecutionId, Attempt, Path), so the ids
    // must be unique per test run or leftovers from an interrupted run swallow the inserts.
    private readonly string _runIdRound1 = $"wf{Guid.NewGuid():N}_block_w_r1_1";
    private readonly string _runIdRound2 = $"wf{Guid.NewGuid():N}_block_w_r2_1";

    private readonly ApiTestFixture _fixture;
    private readonly List<string> _seededWorkflowIds = [];
    private readonly List<string> _seededSectionIds = [];
    private readonly List<string> _seededBlockIds = [];

    public AgentRunOutputPersistenceTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        using IServiceScope scope = _fixture.ClientBuilder.GetNewScope<IServiceScopeFactory>()
            .Service.CreateScope();
        await using UserDataDbContext db = await CreateContextAsync(scope);
        _ = await db.AgentRunOutputs
            .Where(row => _seededBlockIds.Contains(row.BlockId)).ExecuteDeleteAsync();
        _ = await db.AgentRunTraceFiles
            .Where(file => file.BlockId != null && _seededBlockIds.Contains(file.BlockId)).ExecuteDeleteAsync();
        _ = await db.BlockResponses
            .Where(response => response.BlockId != null && _seededBlockIds.Contains(response.BlockId)).ExecuteDeleteAsync();
        _ = await db.Blocks.Where(block => _seededBlockIds.Contains(block.Id)).ExecuteDeleteAsync();
        _ = await db.Sections.Where(section => _seededSectionIds.Contains(section.Id)).ExecuteDeleteAsync();
        _ = await db.Workflows.Where(workflow => _seededWorkflowIds.Contains(workflow.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task PersistBlockResult_ARedoLegsPersist_LeavesRoundOnesRowsIntactAndSettlesOneResponse()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        IServiceScope scope = _fixture.ClientBuilder.GetNewScope<IServiceScopeFactory>()
            .Service.CreateScope();

        try
        {
            IExecutedBlockWriter writer = scope.ServiceProvider.GetRequiredService<IExecutedBlockWriter>();
            Block block = await SeedWorkflowSkeletonAsync(scope, handle.User, cancellationToken);

            RecordAttempt(block, round: 1, _runIdRound1, "round one output");
            AttachTrace(block, _runIdRound1, "trace/summary.md", [1, 2, 3]);
            await writer.PersistBlockResultAsync(block, paragraph: null, executionLog: null, cancellationToken);

            // The loop's redo leg: a fresh round appends beside round one. AttachTraces retains
            // ledgered rounds now, so this test clears by hand to exercise the writer's own
            // skip-persisted-keys dedupe with only the new leg's pack in the working set.
            RecordAttempt(block, round: 2, _runIdRound2, "round two output");
            block.AgentRunTraceFiles.Clear();
            AttachTrace(block, _runIdRound2, "trace/summary.md", [4, 5, 6]);
            await writer.PersistBlockResultAsync(block, paragraph: null, executionLog: null, cancellationToken);

            await using UserDataDbContext db = await CreateContextAsync(scope);

            List<AgentRunOutput> outputs = await db.AgentRunOutputs
                .AsNoTracking()
                .Where(row => row.BlockId == block.Id)
                .OrderBy(row => row.Round)
                .ToListAsync(cancellationToken);
            Assert.Equal(2, outputs.Count);
            Assert.Equal("round one output", outputs[0].Output);
            Assert.Equal(_runIdRound1, outputs[0].RunExecutionId);
            Assert.Equal("round two output", outputs[1].Output);
            Assert.Equal(_runIdRound2, outputs[1].RunExecutionId);

            BlockResponse settled = Assert.Single(await db.BlockResponses
                .AsNoTracking()
                .Where(response => response.BlockId == block.Id)
                .ToListAsync(cancellationToken));
            Assert.Equal("round two output", settled.RawResponse);

            List<AgentRunTraceFile> traces = await db.AgentRunTraceFiles
                .AsNoTracking()
                .Where(file => file.BlockId == block.Id)
                .ToListAsync(cancellationToken);
            Assert.Equal(2, traces.Count);
            _ = Assert.Single(traces, file => file.RunExecutionId == _runIdRound1);
            _ = Assert.Single(traces, file => file.RunExecutionId == _runIdRound2);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task PersistBlockResult_ReplayingTheSameRound_UpdatesItsRowInPlaceInsteadOfAddingASecond()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();
        IServiceScope scope = _fixture.ClientBuilder.GetNewScope<IServiceScopeFactory>()
            .Service.CreateScope();

        try
        {
            IExecutedBlockWriter writer = scope.ServiceProvider.GetRequiredService<IExecutedBlockWriter>();
            Block block = await SeedWorkflowSkeletonAsync(scope, handle.User, cancellationToken);

            RecordAttempt(block, round: 1, _runIdRound1, "first materialization");
            await writer.PersistBlockResultAsync(block, paragraph: null, executionLog: null, cancellationToken);

            // A crash-replayed re-execution re-records the round under a fresh row id.
            block.AgentRunOutputs.Clear();
            RecordAttempt(block, round: 1, _runIdRound1 + "-replay", "replayed materialization");
            await writer.PersistBlockResultAsync(block, paragraph: null, executionLog: null, cancellationToken);

            await using UserDataDbContext db = await CreateContextAsync(scope);
            AgentRunOutput row = Assert.Single(await db.AgentRunOutputs
                .AsNoTracking()
                .Where(candidate => candidate.BlockId == block.Id)
                .ToListAsync(cancellationToken));
            Assert.Equal(1, row.Round);
            Assert.Equal("replayed materialization", row.Output);
            Assert.Equal(_runIdRound1 + "-replay", row.RunExecutionId);
        }
        finally
        {
            scope.Dispose();
        }
    }

    private static void RecordAttempt(Block block, int round, string runExecutionId, string output)
    {
        block.AgentRunOutputs.Add(new AgentRunOutput
        {
            OrganizationId = ORGANIZATION_ID,
            BlockId = block.Id,
            Block = block,
            Round = round,
            RunExecutionId = runExecutionId,
            Output = output,
        });
        _ = new BlockResponse(block) { RawResponse = output };
    }

    private static void AttachTrace(Block block, string runExecutionId, string path, byte[] content)
    {
        block.AgentRunTraceFiles.Add(new AgentRunTraceFile
        {
            OrganizationId = ORGANIZATION_ID,
            BlockId = block.Id,
            Block = block,
            RunExecutionId = runExecutionId,
            Attempt = 1,
            Path = path,
            Sha256 = new string('a', 64),
            SizeBytes = content.LongLength,
            Content = content,
        });
    }

    private async Task<Block> SeedWorkflowSkeletonAsync(
        IServiceScope scope,
        User user,
        CancellationToken cancellationToken)
    {
        var root = new Workflow
        {
            Id = Guid.NewGuid().ToString(),
            Title = "attempt-durability-root",
            OrganizationId = user.OrganizationId,
            UserId = user.Id,
        };
        _seededWorkflowIds.Add(root.Id);

        var section = new Section("attempt-durability", root)
        {
            Id = Guid.NewGuid().ToString(),
            Title = "attempt-durability",
        };
        _seededSectionIds.Add(section.Id);

        var block = new Block(BlockTypes.Agent, section)
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Member",
        };
        _seededBlockIds.Add(block.Id);

        await using UserDataDbContext db = await CreateContextAsync(scope);
        _ = db.Workflows.Add(root);
        _ = await db.SaveChangesAsync(cancellationToken);

        return block;
    }

    private static async Task<UserDataDbContext> CreateContextAsync(IServiceScope scope)
    {
        IDbContextFactory<UserDataDbContext> factory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();

        return await factory.CreateDbContextAsync();
    }
}
