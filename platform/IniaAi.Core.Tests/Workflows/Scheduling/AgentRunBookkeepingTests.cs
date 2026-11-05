using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// Guards the leaked-run contract end to end through the real handler and the real block
/// writer: the moment an agent run starts — before the first poll — the persisted block row
/// already names the run id, the pass, and the unacknowledged marker, so a process death
/// mid-poll leaves a row that terminal-transition cleanup and the janitor can enumerate.
/// </summary>
public class AgentRunBookkeepingTests : IClassFixture<WorkflowsFixture<AgentRunBookkeepingTests>>
{
    private const string ID_PREFIX = "arb";
    private const string RUN_SUFFIX = "_w_r1_1";

    private readonly IServiceProvider _sp;

    public AgentRunBookkeepingTests(
        WorkflowsFixture<AgentRunBookkeepingTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task AgentBlockRun_PersistsRunBookkeepingBeforeTheFirstPoll_AndSettlesItAcknowledged()
    {
        StubAgentRuntimeClient stub = ArrangeSucceedingRun();
        List<AgentRunPass>? persistedAtFirstPoll = null;
        stub.OnGetRun = executionId =>
        {
            persistedAtFirstPoll ??= ReadPersistedRunPasses(ExecutedBlockIdOf(executionId));

            return SucceededMonitor(executionId);
        };

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);

        await TestWorkflowRuns.RunWithOutputAsync(
            _sp, handler, BuildAgentWorkflow(), TestContext.Current.CancellationToken);

        string executionId = Assert.Single(stub.StartRequests).ExecutionId;
        Assert.StartsWith($"{handler.WorkflowExecutionId}_", executionId);
        Assert.EndsWith(RUN_SUFFIX, executionId);

        Assert.NotNull(persistedAtFirstPoll);
        AgentRunPass atStart = Assert.Single(persistedAtFirstPoll!);
        Assert.Equal(1, atStart.Pass);
        Assert.Equal(executionId, atStart.ExecutionId);
        Assert.True(atStart.Unacknowledged);
        Assert.Null(atStart.Status);

        AgentRunPass atSettle = Assert.Single(ReadPersistedRunPasses(ExecutedBlockIdOf(executionId)));
        Assert.Equal(executionId, atSettle.ExecutionId);
        Assert.Equal(nameof(AgentRunStatus.Succeeded), atSettle.Status);
        Assert.False(atSettle.Unacknowledged);
    }

    [Fact]
    public async Task AgentBlockRun_WithheldComplete_PersistsTheRunAsLeftUnacknowledged()
    {
        StubAgentRuntimeClient stub = ArrangeSucceedingRun();
        stub.OnListRunFiles = _ => Error.Unexpected("AgentRun.Unavailable", "listing failed");

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);

        await TestWorkflowRuns.RunWithOutputAsync(
            _sp, handler, BuildAgentWorkflow(), TestContext.Current.CancellationToken);

        string executionId = Assert.Single(stub.StartRequests).ExecutionId;
        Assert.Empty(stub.CompletedExecutionIds);

        AgentRunPass persisted = Assert.Single(ReadPersistedRunPasses(ExecutedBlockIdOf(executionId)));
        Assert.Equal(executionId, persisted.ExecutionId);
        Assert.Equal(nameof(AgentRunStatus.Succeeded), persisted.Status);
        Assert.True(persisted.Unacknowledged);
    }

    [Fact]
    public async Task AgentBlockRun_FailedBookkeepingFlush_AbandonsTheRunBeforeSpendInsteadOfTaintingResults()
    {
        StubAgentRuntimeClient stub = ArrangeSucceedingRun();
        int polls = 0;
        stub.OnGetRun = executionId =>
        {
            _ = Interlocked.Increment(ref polls);

            return SucceededMonitor(executionId);
        };

        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(call =>
        {
            var block = (Block)call[0];

            return block.Properties is AgentProperties { ExecutionState.RunPasses: [{ Status: null }] }
                ? throw new InvalidOperationException("transient store blip")
                : Task.CompletedTask;
        });
        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer);

        CriticalBlockFailureException abort = await Assert.ThrowsAsync<CriticalBlockFailureException>(
            () => TestWorkflowRuns.RunWithOutputAsync(
                _sp, handler, BuildAgentWorkflow(), TestContext.Current.CancellationToken));

        Assert.Equal(0, polls);
        _ = Assert.Single(stub.CancelledExecutionIds);
        Assert.Empty(stub.CompletedExecutionIds);
        Assert.Empty(abort.RecoveredSiblingResults);
        Assert.Empty(abort.LostSiblingResults);
    }

    /// <summary>
    /// A start that registers, one poll to Succeeded, an acknowledgeable empty trace pack.
    /// Tests override the verb they are about.
    /// </summary>
    private StubAgentRuntimeClient ArrangeSucceedingRun()
    {
        var stub = Assert.IsType<StubAgentRuntimeClient>(_sp.GetRequiredService<IAgentRuntimeClient>());

        stub.OnStartRun = request => new AgentRunMonitor
        {
            ExecutionId = request.ExecutionId,
            Status = AgentRunStatus.Running,
            Attempt = 1,
            PollInterval = TimeSpan.FromMilliseconds(1),
        };
        stub.OnGetRun = SucceededMonitor;
        stub.OnCompleteRun = executionId => new AgentRunMonitor
        {
            ExecutionId = executionId,
            Status = AgentRunStatus.Succeeded,
            Attempt = 1,
        };
        stub.OnListRunFiles = _ => ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>(
            Array.Empty<AgentRunFileEntry>());

        return stub;
    }

    private static ErrorOr<AgentRunMonitor> SucceededMonitor(string executionId)
    {
        return new AgentRunMonitor
        {
            ExecutionId = executionId,
            Status = AgentRunStatus.Succeeded,
            Attempt = 1,
            Result = new AgentRunResult("agent output", null),
        };
    }

    private static string ExecutedBlockIdOf(string executionId)
    {
        return TestWorkflowRuns.ExecutedBlockIdOf(executionId);
    }

    private List<AgentRunPass> ReadPersistedRunPasses(string blockId)
    {
        Block persisted = TestWorkflowRuns.ReadPersistedBlock(_sp, blockId);
        var props = Assert.IsType<AgentProperties>(persisted.Properties);

        return props.ExecutionState?.RunPasses ?? [];
    }

    private static Workflow BuildAgentWorkflow()
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
        });
    }
}
