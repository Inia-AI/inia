using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunTerminalCleanupTests : IDisposable
{
    private const string ORG_ID = "org-cleanup";
    private const string EXECUTION_ID = "cleanup-exec-1";

    private readonly DbContextOptions<UserDataDbContext> _options;
    private readonly RecordingAgentRunExecutor _executor = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly AgentRunTerminalCleanup _cleanup;

    public AgentRunTerminalCleanupTests()
    {
        _options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"AgentRunTerminalCleanupTests-{Guid.NewGuid()}")
            .Options;

        var services = new ServiceCollection();
        _ = services.AddScoped(_ => new UserDataDbContext(_options));
        _ = services.AddSingleton<IAgentRunExecutor>(_executor);
        _serviceProvider = services.BuildServiceProvider();

        _cleanup = new AgentRunTerminalCleanup(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunTerminalCleanup>.Instance);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CleanUpAsync_SettlesEveryUnacknowledgedRunThroughTheSharedTeardown()
    {
        await SeedSnapshotAsync(
            AgentBlock("blk-a", ("exec_blk-a_w_1", true, null), ("exec_blk-a_w_2", false, "Succeeded")),
            AgentBlock("blk-b", ("exec_blk-b_w_1", true, null)),
            BlockOf("blk-m", BlockTypes.Merge, new MergeProperties { TextTemplate = "text" }),
            BlockOf("blk-c", BlockTypes.Agent, new AgentProperties { Model = "test-model-a" }));

        await _cleanup.CleanUpAsync(EXECUTION_ID, ORG_ID, WorkflowState.Failed);

        Assert.Equal(
            ["exec_blk-a_w_1", "exec_blk-b_w_1"],
            _executor.Settled.Select(settled => settled.ExecutionId).Order().ToArray());
    }

    [Fact]
    public async Task CleanUpAsync_AVerifierBlocksLeakedRun_IsSettledLikeAWorkers()
    {
        await SeedSnapshotAsync(
            AgentBlock("blk-a", ("exec_blk-a_w_1", true, null)),
            VerificationBlock("blk-v", ("exec_blk-v_v_1", true, null), ("exec_blk-v_v_2", false, "Succeeded")));

        await _cleanup.CleanUpAsync(EXECUTION_ID, ORG_ID, WorkflowState.Failed);

        Assert.Equal(
            ["exec_blk-a_w_1", "exec_blk-v_v_1"],
            _executor.Settled.Select(settled => settled.ExecutionId).Order().ToArray());
    }

    [Fact]
    public async Task CleanUpAsync_AnchorsEachRunsEvidenceToItsOwnBlockAndTheOrganization()
    {
        await SeedSnapshotAsync(
            AgentBlock("blk-a", ("exec_blk-a_w_1", true, null)),
            AgentBlock("blk-b", ("exec_blk-b_w_1", true, null)));

        await _cleanup.CleanUpAsync(EXECUTION_ID, ORG_ID, WorkflowState.Failed);

        Assert.Equal(2, _executor.Settled.Count);
        Assert.All(_executor.Settled, settled =>
        {
            Assert.NotNull(settled.Anchor);
            Assert.Equal(ORG_ID, settled.Anchor.OrganizationId);
        });
        Assert.Equal(
            [("exec_blk-a_w_1", "blk-a"), ("exec_blk-b_w_1", "blk-b")],
            _executor.Settled
                .Select(settled => (settled.ExecutionId, settled.Anchor!.BlockId))
                .Order()
                .ToArray());
    }

    [Fact]
    public async Task CleanUpAsync_SettledButWithheldRuns_StayForTheRetentionReaper()
    {
        await SeedSnapshotAsync(AgentBlock(
            "blk-a",
            ("exec_leak_w_1", true, null),
            ("exec_live_w_2", true, "Running"),
            ("exec_withheld_w_3", true, "Failed"),
            ("exec_unreadable_w_4", true, "Unknown")));

        await _cleanup.CleanUpAsync(EXECUTION_ID, ORG_ID, WorkflowState.Failed);

        Assert.Equal(
            ["exec_leak_w_1", "exec_live_w_2"],
            _executor.Settled.Select(settled => settled.ExecutionId).Order().ToArray());
    }

    [Fact]
    public async Task CleanUpAsync_SnapshotMissingItsOrganizationStamp_StillSettlesTheRuns()
    {
        await SeedSnapshotAsync(
            snapshotOrganizationId: null,
            AgentBlock("blk-a", ("exec_blk-a_w_1", true, null)));

        await _cleanup.CleanUpAsync(EXECUTION_ID, ORG_ID, WorkflowState.Failed);

        Assert.Equal(["exec_blk-a_w_1"], _executor.Settled.Select(settled => settled.ExecutionId).ToArray());
    }

    [Fact]
    public async Task CleanUpAsync_ExecutorThrowing_NeverEscapes()
    {
        await SeedSnapshotAsync(AgentBlock("blk-a", ("exec_blk-a_w_1", true, null)));
        _executor.SettleResult = _ => throw new InvalidOperationException("runtime down");

        Exception? exception = await Record.ExceptionAsync(
            () => _cleanup.CleanUpAsync(EXECUTION_ID, ORG_ID, WorkflowState.Failed));

        Assert.Null(exception);
    }

    [Fact]
    public async Task CleanUpAsync_NoSnapshotForTheExecution_IssuesNothingAndDoesNotThrow()
    {
        Exception? exception = await Record.ExceptionAsync(
            () => _cleanup.CleanUpAsync("never-persisted", ORG_ID, WorkflowState.Failed));

        Assert.Null(exception);
        Assert.Empty(_executor.Settled);
    }

    [Fact]
    public async Task CleanUpAsync_OrganizationMismatch_IssuesNothing()
    {
        await SeedSnapshotAsync(AgentBlock("blk-a", ("exec_blk-a_w_1", true, null)));

        await _cleanup.CleanUpAsync(EXECUTION_ID, "some-other-org", WorkflowState.Failed);

        Assert.Empty(_executor.Settled);
    }

    private Task SeedSnapshotAsync(params Action<Section>[] addBlocks)
    {
        return SeedSnapshotAsync(snapshotOrganizationId: ORG_ID, addBlocks);
    }

    private async Task SeedSnapshotAsync(string? snapshotOrganizationId, params Action<Section>[] addBlocks)
    {
        var root = new Workflow
        {
            Id = EXECUTION_ID,
            Name = "snapshot",
            OrganizationId = snapshotOrganizationId,
        };
        var section = new Section("S", root) { Id = $"{EXECUTION_ID}-sec", Order = 0 };
        foreach (Action<Section> addBlock in addBlocks)
        {
            addBlock(section);
        }

        using var context = new UserDataDbContext(_options);
        _ = context.Add(root);
        _ = context.WorkflowExecutions.Add(new WorkflowExecution
        {
            Id = EXECUTION_ID,
            OrganizationId = ORG_ID,
            Title = "cleanup test execution",
        });
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static Action<Section> BlockOf(string blockId, BlockTypes type, BlockProperties properties)
    {
        return section => _ = new Block(type, section, replacementTag: blockId)
        {
            Id = blockId,
            Name = blockId,
            Properties = properties,
        };
    }

    private static Action<Section> AgentBlock(
        string blockId,
        params (string RunExecutionId, bool Unacknowledged, string? Status)[] passes)
    {
        var properties = new AgentProperties { Model = "test-model-a" };
        properties.SetExecutionState(true, []);
        for (int i = 0; i < passes.Length; i++)
        {
            properties.ExecutionState!.RunPasses.Add(new AgentRunPass
            {
                Pass = i + 1,
                ExecutionId = passes[i].RunExecutionId,
                Unacknowledged = passes[i].Unacknowledged,
                Status = passes[i].Status,
            });
        }

        return BlockOf(blockId, BlockTypes.Agent, properties);
    }

    private static Action<Section> VerificationBlock(
        string blockId,
        params (string RunExecutionId, bool Unacknowledged, string? Status)[] passes)
    {
        var properties = new VerificationProperties
        {
            Members = [new VerificationMember { MemberTag = "member" }],
            Criteria = [new VerificationCriterion { Id = "criterion", Label = "Criterion" }],
            Model = "test-model-a",
        };
        properties.SetExecutionState(true, []);
        for (int i = 0; i < passes.Length; i++)
        {
            properties.ExecutionState!.RunPasses.Add(new AgentRunPass
            {
                Pass = i + 1,
                ExecutionId = passes[i].RunExecutionId,
                Unacknowledged = passes[i].Unacknowledged,
                Status = passes[i].Status,
            });
        }

        return BlockOf(blockId, BlockTypes.Verification, properties);
    }

    private sealed class RecordingAgentRunExecutor : IAgentRunExecutor
    {
        public List<(string ExecutionId, AgentRunTraceAnchor? Anchor)> Settled { get; } = [];
        public Func<string, bool> SettleResult { get; set; } = _ => true;

        public Task<ErrorOr<AgentRunConclusion>> ExecuteAsync(
            AgentRunStartRequest request,
            AgentRunExecutionOptions options,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Cleanup never starts runs");
        }

        public Task<bool> AcknowledgeQuietlyAsync(string executionId)
        {
            throw new NotSupportedException("Cleanup settles through the shared teardown");
        }

        public Task CancelQuietlyAsync(string executionId)
        {
            throw new NotSupportedException("Cleanup settles through the shared teardown");
        }

        public Task<bool> SettleAbandonedQuietlyAsync(string executionId, AgentRunTraceAnchor? anchor)
        {
            Settled.Add((executionId, anchor));
            return Task.FromResult(SettleResult(executionId));
        }

        public Task<AgentRunMonitor> AbandonQuietlyAsync(string executionId, AgentRunTraceAnchor? anchor)
        {
            Settled.Add((executionId, anchor));
            return Task.FromResult(new AgentRunMonitor { ExecutionId = executionId, Status = AgentRunStatus.Cancelled });
        }
    }

    /// <summary>
    /// The terminal decides how much custody survives: a failure keeps what each block last
    /// reached, and every other terminal — the successful one included — releases the lot.
    /// </summary>
    [Theory]
    [InlineData(WorkflowState.Failed, AgentRunSessionBundleRelease.ExceptTheLastLegOfEachBlock)]
    [InlineData(WorkflowState.Cancelled, AgentRunSessionBundleRelease.Everything)]
    [InlineData(WorkflowState.Draft, AgentRunSessionBundleRelease.Everything)]
    [InlineData(WorkflowState.Confirmed, AgentRunSessionBundleRelease.Everything)]
    public async Task CleanUpAsync_ReleasesSessionBundlesByTerminal(
        WorkflowState terminalState,
        AgentRunSessionBundleRelease expected)
    {
        await SeedSnapshotAsync(AgentBlock("blk-a", ("exec_blk-a_w_1", false, "Succeeded")));
        var reaper = new RecordingBundleReaper();
        var cleanup = new AgentRunTerminalCleanup(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunTerminalCleanup>.Instance,
            reaper);

        await cleanup.CleanUpAsync(EXECUTION_ID, ORG_ID, terminalState);

        Assert.Equal([(EXECUTION_ID, ORG_ID, expected)], reaper.Released);
    }

    /// <summary>
    /// Bundles are released whether or not any run needs settling — the two halves answer
    /// different questions, and an execution can hold custody with a clean run ledger.
    /// </summary>
    [Fact]
    public async Task CleanUpAsync_NoRunNeedsSettling_StillReleasesSessionBundles()
    {
        await SeedSnapshotAsync(
            AgentBlock("blk-a", ("exec_blk-a_w_1", false, "Succeeded")));
        var reaper = new RecordingBundleReaper();
        var cleanup = new AgentRunTerminalCleanup(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunTerminalCleanup>.Instance,
            reaper);

        await cleanup.CleanUpAsync(EXECUTION_ID, ORG_ID, WorkflowState.Draft);

        Assert.Empty(_executor.Settled);
        _ = Assert.Single(reaper.Released);
    }

    /// <summary>
    /// Only an agent-family block can hold a bundle, and every execution in the deployment
    /// reaches this on its terminal — so an execution with no agent block asks storage nothing.
    /// </summary>
    [Fact]
    public async Task CleanUpAsync_ExecutionWithNoAgentBlock_ReleasesNothing()
    {
        await SeedSnapshotAsync(
            BlockOf("blk-m", BlockTypes.Merge, new MergeProperties { TextTemplate = "text" }));
        var reaper = new RecordingBundleReaper();
        var cleanup = new AgentRunTerminalCleanup(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunTerminalCleanup>.Instance,
            reaper);

        await cleanup.CleanUpAsync(EXECUTION_ID, ORG_ID, WorkflowState.Failed);

        Assert.Empty(reaper.Released);
    }

    /// <summary>
    /// A state that is not a terminal releases nothing: the mapping is over the four terminals,
    /// not "anything that is not Failed", so a caller passing a live state cannot take the
    /// custody of an execution still running.
    /// </summary>
    [Theory]
    [InlineData(WorkflowState.Running)]
    [InlineData(WorkflowState.Queued)]
    [InlineData(WorkflowState.ProcessingDocuments)]
    [InlineData(WorkflowState.Undefined)]
    public async Task CleanUpAsync_StateThatIsNotATerminal_ReleasesNothing(WorkflowState liveState)
    {
        await SeedSnapshotAsync(AgentBlock("blk-a", ("exec_blk-a_w_1", false, "Succeeded")));
        var reaper = new RecordingBundleReaper();
        var cleanup = new AgentRunTerminalCleanup(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunTerminalCleanup>.Instance,
            reaper);

        await cleanup.CleanUpAsync(EXECUTION_ID, ORG_ID, liveState);

        Assert.Empty(reaper.Released);
    }

    private sealed class RecordingBundleReaper : IAgentRunSessionBundleReaper
    {
        public List<(string ExecutionId, string OrganizationId, AgentRunSessionBundleRelease Release)> Released { get; } = [];

        public Task ReleaseExecutionAsync(
            string workflowExecutionId,
            string organizationId,
            AgentRunSessionBundleRelease release)
        {
            Released.Add((workflowExecutionId, organizationId, release));

            return Task.CompletedTask;
        }

        public Task ReleaseConversationBeforeAsync(string conversationId, int keepFromTurn)
        {
            return Task.CompletedTask;
        }

        public Task<int> SweepExpiredAsync(DateTime createdBefore, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
