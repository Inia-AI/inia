using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Agents;

/// <summary>
/// Pins the write discipline of the per-attempt durability home: recording an attempt never
/// touches the block's response — only settling does — and rows append across rounds while an
/// editor re-run of the same round replaces only its own row.
/// </summary>
public class AgentRunAttemptOutputTests : IClassFixture<WorkflowsFixture<AgentRunAttemptOutputTests>>
{
    private const string ORGANIZATION_ID = "org-attempts";

    private readonly IServiceProvider _testServiceProvider;

    public AgentRunAttemptOutputTests(
        WorkflowsFixture<AgentRunAttemptOutputTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
    }

    private (Block Block, BlockExecutionContext Context) Arrange()
    {
        var block = new Block(BlockTypes.Agent, new Section("test", new Workflow("test")));
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        context.OrganizationId = ORGANIZATION_ID;

        return (block, context);
    }

    [Fact]
    public void RecordAttemptOutput_LeavesTheResponseUntouched_UntilSettleSelectsAnAttempt()
    {
        (Block block, BlockExecutionContext context) = Arrange();

        AgentRunOutput attempt = AgentRunBlockSupport.RecordAttemptOutput(
            block, context, round: 1, "wf_b_w_r1_1", "first");

        Assert.Null(block.BlockResponse);
        Assert.Equal(block.Id, attempt.BlockId);
        Assert.Equal(ORGANIZATION_ID, attempt.OrganizationId);

        AgentRunBlockSupport.SettleResponse(block, attempt);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal("first", block.BlockResponse.RawResponse);
    }

    [Fact]
    public void RecordAttemptOutput_ASecondRound_AppendsAndLeavesRoundOneIntact()
    {
        (Block block, BlockExecutionContext context) = Arrange();
        AgentRunOutput first = AgentRunBlockSupport.RecordAttemptOutput(
            block, context, round: 1, "wf_b_w_r1_1", "first");

        _ = AgentRunBlockSupport.RecordAttemptOutput(block, context, round: 2, "wf_b_w_r2_1", "second");

        Assert.Equal(2, block.AgentRunOutputs.Count);
        Assert.Contains(first, block.AgentRunOutputs);
        Assert.Equal("first", Assert.Single(block.AgentRunOutputs, row => row.Round == 1).Output);
        Assert.Equal("second", Assert.Single(block.AgentRunOutputs, row => row.Round == 2).Output);
    }

    [Fact]
    public void RecordAttemptOutput_OutputWithNulCharacters_IsStrippedLikeEveryPersistedOutputColumn()
    {
        (Block block, BlockExecutionContext context) = Arrange();

        AgentRunOutput attempt = AgentRunBlockSupport.RecordAttemptOutput(
            block, context, round: 1, "wf_b_w_r1_1", "before\0after");

        Assert.Equal("beforeafter", attempt.Output);
    }

    [Fact]
    public void RecordAttemptOutput_TheSameRoundAgain_ReplacesItsOwnRowOnly()
    {
        (Block block, BlockExecutionContext context) = Arrange();
        _ = AgentRunBlockSupport.RecordAttemptOutput(block, context, round: 1, "a_b_w_r1_1", "first");
        _ = AgentRunBlockSupport.RecordAttemptOutput(block, context, round: 2, "a_b_w_r2_1", "second");

        AgentRunOutput replaced = AgentRunBlockSupport.RecordAttemptOutput(
            block, context, round: 2, "b_b_w_r2_1", "second again");

        Assert.Equal(2, block.AgentRunOutputs.Count);
        Assert.Equal("first", Assert.Single(block.AgentRunOutputs, row => row.Round == 1).Output);
        AgentRunOutput kept = Assert.Single(block.AgentRunOutputs, row => row.Round == 2);
        Assert.Same(replaced, kept);
        Assert.Equal("second again", kept.Output);
        Assert.Equal("b_b_w_r2_1", kept.RunExecutionId);
    }
}
