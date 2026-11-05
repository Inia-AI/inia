using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using static IniaAi.Core.Tests.Workflows.Scheduling.Helpers.VerifiedGroupRuns;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// A block that ran but is not a report output source still did its work, so its log row states
/// the model and the time it took rather than reading like a skipped block.
/// </summary>
public class BlockLogWithoutReportOutputTests : IClassFixture<WorkflowsFixture<BlockLogWithoutReportOutputTests>>
{
    private const string AGENT_NAME = "Unreported agent";

    private readonly IServiceProvider _sp;

    public BlockLogWithoutReportOutputTests(
        WorkflowsFixture<BlockLogWithoutReportOutputTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task AnAgentBlockThatIsNotAnOutputSource_LogsItsModelAndDuration()
    {
        _ = ArrangeGroupRuns(
            _sp,
            verifierOutput: static _ => throw new InvalidOperationException("No verifier in this workflow."),
            workerOutput: static _ => "42");

        Workflow root = new() { Id = "unreported-root", Title = "unreported" };
        Section section = new("S", root) { Id = "unreported-sec", Order = 0 };
        _ = new Block(BlockTypes.Agent, section, replacementTag: "unreported_agent")
        {
            Id = "unreported-agent",
            Name = AGENT_NAME,
            Properties = WorkerProperties("Reply with 42."),
            IsOutputSource = false,
        };

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);
        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, root, TestContext.Current.CancellationToken);

        WorkflowExecutionLog log = Assert.Single(ReadLogs(_sp, handler.WorkflowExecutionId!, AGENT_NAME));
        Assert.Equal(BlockExecutionStatus.Completed, log.Status);
        Assert.Equal(MODEL, log.ModelUsed);
        Assert.True(log.Duration > TimeSpan.Zero);
        Assert.Equal("42", log.Output);
    }

    [Fact]
    public async Task ABlockThatCallsNoModel_LogsNoModel()
    {
        Workflow root = new() { Id = "nomodel-root", Title = "nomodel" };
        Section section = new("S", root) { Id = "nomodel-sec", Order = 0 };
        _ = new Block(BlockTypes.StringManipulation, section, replacementTag: "plain_text")
        {
            Id = "nomodel-text",
            Name = "Plain text",
            Properties = new StringManipulationProperties
            {
                InputTemplate = "hello",
                Find = "unused",
                ReplaceWith = "unused",
            },
        };

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);
        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, root, TestContext.Current.CancellationToken);

        WorkflowExecutionLog log = Assert.Single(ReadLogs(_sp, handler.WorkflowExecutionId!, "Plain text"));
        Assert.Equal(BlockExecutionStatus.Completed, log.Status);
        Assert.Null(log.ModelUsed);
    }
}
