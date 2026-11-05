using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// A block whose strategy reports its own failure, instead of throwing, still ends the run when
/// the block is set to stop the run, and leaves the run going when it is not.
/// </summary>
public class StrategyReportedFailureTests : IClassFixture<WorkflowsFixture<StrategyReportedFailureTests>>
{
    private const string ID_PREFIX = "srf";
    private const string BLOCK_NAME = "B0";
    private const string NOT_JSON = "No document was uploaded.";
    private const string START_REFUSED = "The agent service refused the run.";

    private readonly IServiceProvider _sp;

    public StrategyReportedFailureTests(
        WorkflowsFixture<StrategyReportedFailureTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task MergeJsonWithNoValidInput_Critical_EndsTheRunAsACriticalBlockFailure()
    {
        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);

        CriticalBlockFailureException ex = await Assert.ThrowsAsync<CriticalBlockFailureException>(
            () => TestWorkflowRuns.RunWithOutputAsync(
                _sp, handler, BuildMergeJsonWorkflow(isCritical: true), TestContext.Current.CancellationToken));

        Assert.Equal(WorkflowExecutionFailureKinds.CRITICAL_BLOCK_FAILURE, ex.FailureKind);
        Assert.Contains(BLOCK_NAME, ex.FailureDetail);
        Assert.Contains("had no valid JSON input", ex.FailureDetail);
    }

    [Fact]
    public async Task MergeJsonWithNoValidInput_NotCritical_LetsTheRunComplete()
    {
        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);

        await TestWorkflowRuns.RunWithOutputAsync(
            _sp, handler, BuildMergeJsonWorkflow(isCritical: null), TestContext.Current.CancellationToken);

        WorkflowExecutionLog log = await ReadBlockLogAsync(handler);
        Assert.Equal(BlockExecutionStatus.Completed, log.Status);
        Assert.Equal("{}", log.Output);
    }

    [Fact]
    public async Task FailedAgentRun_SetNotToStopTheRun_LetsTheRunComplete()
    {
        StubAgentRuntimeClient stub = ArrangeRefusedStart();
        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);

        await TestWorkflowRuns.RunWithOutputAsync(
            _sp, handler, BuildAgentWorkflow(isCritical: false), TestContext.Current.CancellationToken);

        _ = Assert.Single(stub.StartRequests);
        WorkflowExecutionLog log = await ReadBlockLogAsync(handler);
        Assert.Equal(BlockExecutionStatus.Failed, log.Status);
    }

    [Fact]
    public async Task FailedAgentRun_ByDefault_EndsTheRunAsACriticalBlockFailure()
    {
        StubAgentRuntimeClient stub = ArrangeRefusedStart();
        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);

        CriticalBlockFailureException ex = await Assert.ThrowsAsync<CriticalBlockFailureException>(
            () => TestWorkflowRuns.RunWithOutputAsync(
                _sp, handler, BuildAgentWorkflow(isCritical: null), TestContext.Current.CancellationToken));

        _ = Assert.Single(stub.StartRequests);
        Assert.Equal(WorkflowExecutionFailureKinds.CRITICAL_BLOCK_FAILURE, ex.FailureKind);
    }

    private async Task<WorkflowExecutionLog> ReadBlockLogAsync(WorkflowHandler handler)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        await using UserDataDbContext read = await factory.CreateDbContextAsync(ct);

        return Assert.Single(
            await read.Set<WorkflowExecutionLog>().AsNoTracking()
                .Where(l => l.WorkflowExecutionId == handler.WorkflowExecutionId && l.BlockName == BLOCK_NAME)
                .ToListAsync(ct));
    }

    private StubAgentRuntimeClient ArrangeRefusedStart()
    {
        var stub = Assert.IsType<StubAgentRuntimeClient>(_sp.GetRequiredService<IAgentRuntimeClient>());
        stub.OnStartRun = _ => Error.Failure("AgentRun.Refused", START_REFUSED);

        return stub;
    }

    private static Workflow BuildMergeJsonWorkflow(bool? isCritical)
    {
        return TestWorkflowRuns.BuildSingleBlockWorkflow(ID_PREFIX, BlockTypes.MergeJson, new MergeJsonProperties
        {
            JsonInputs = [NOT_JSON],
            IsCritical = isCritical,
        });
    }

    private static Workflow BuildAgentWorkflow(bool? isCritical)
    {
        return TestWorkflowRuns.BuildSingleBlockWorkflow(ID_PREFIX, BlockTypes.Agent, new AgentProperties
        {
            WorkerSkills =
            [
                new AgentWorkerSkill
                {
                    Name = "skill",
                    Description = "Handles the work.",
                    Content = "work",
                },
            ],
            Model = "claude-sonnet-4-6",
            IsCritical = isCritical,
        });
    }
}
