using IniaAi.Core.Tests.Workflows.Helpers;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing;
using Microsoft.Extensions.DependencyInjection;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// The headline unit-permit regression. A stub node that dispatches billable units of its own inside
/// its own execution must complete at every cap instead of waiting on a permit its own node holds.
/// Against the pre-change engine the same shape hangs; every case here carries both an explicit
/// CancelAfter and a per-test timeout so a regression is a red test, never a hung suite.
/// </summary>
public class NestedUnitDispatchTests : IClassFixture<WorkflowsFixture<NestedUnitDispatchTests>>
{
    private const string ID_PREFIX = "nested";
    private const int BLOCK_COUNT = 4;
    private const int LEG_COUNT = 3;
    private const int ENGINE_TIMEOUT_MS = 30_000;
    private const int ENGINE_CANCEL_AFTER_MS = 25_000;

    private readonly IServiceProvider _sp;

    public NestedUnitDispatchTests(
        WorkflowsFixture<NestedUnitDispatchTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Theory(Timeout = ENGINE_TIMEOUT_MS)]
    [InlineData(1)]
    [InlineData(4)]
    public async Task NodeThatDispatchesItsOwnUnits_CompletesAtEveryCap(int maxParallelism)
    {
        TestCap cap = new(maxParallelism);
        Workflow workflow = TestWorkflowRuns.BuildParallelWorkflow(ID_PREFIX, BLOCK_COUNT);

        await RunWithStubNodeAsync(cap, workflow);

        foreach (Block block in workflow.Sections.SelectMany(s => s.Blocks))
        {
            Assert.Equal(ExecutionStatus.Completed, block.ExecutionState?.Status);
        }

        // The nodes take none, so a node that had kept its own permit would show 16, not 12.
        Assert.Equal(BLOCK_COUNT * LEG_COUNT, cap.Acquired);
        Assert.Equal(cap.Acquired, cap.Released);
        Assert.Equal(0, cap.CurrentlyHeld);
        Assert.True(
            cap.MaxConcurrentlyHeld <= maxParallelism,
            $"legs exceeded the cap: {cap.MaxConcurrentlyHeld} held at once with a cap of {maxParallelism}.");
    }

    private async Task RunWithStubNodeAsync(TestCap cap, Workflow workflow)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(ENGINE_CANCEL_AFTER_MS));

        LevelScheduler scheduler = TestSchedulers.NewSchedulerWithCap(cap);
        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, scheduler);
        _ = TestBlockStrategies.Swap(handler, BlockTypes.Merge, new NestedDispatchStubStrategy(LEG_COUNT));

        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        // No CreateRootOutputAsync: RootOutput stays null, so the fence probe returns without
        // opening a scope and nothing persists. The permit shape is what is under test.
        _ = await handler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: new(),
            organizationId: TestWorkflowRuns.ORG_ID,
            jobId: Guid.NewGuid().ToString(),
            unitOfWork,
            inputs: [],
            cancellationToken: cts.Token);
    }

    private sealed class NestedDispatchStubStrategy : IBlockStrategy
    {
        private readonly int _legCount;

        public NestedDispatchStubStrategy(int legCount)
        {
            _legCount = legCount;
        }

        public bool DispatchesNestedUnits(Block block)
        {
            return true;
        }

        public async Task<BlockStrategyOutput> ExecuteBlockAsync(
            Block block,
            BlockExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Task[] legs = [.. Enumerable.Range(0, _legCount)
                .Select(_ => context.UnitPermits.RunUnitAsync(
                    legCancellationToken => SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(20), legCancellationToken),
                    cancellationToken))];

            await Task.WhenAll(legs);

            return new BlockStrategyOutput();
        }
    }
}
