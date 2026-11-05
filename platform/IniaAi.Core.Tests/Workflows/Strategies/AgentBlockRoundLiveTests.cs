using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Core.Tests.Workflows.Strategies;

// The identity half of the live gate: proves the real runtime accepts a second verification leg
// as its own run — a reused id would hit the frozen-terminal registry and replay leg one — and
// that leg one's bookkeeping survives the redo entry. Same gating as AgentBlockStrategyLiveTests,
// because these runs are billed.
public class AgentBlockRoundLiveTests : IClassFixture<WorkflowsFixture<AgentBlockRoundLiveTests>>
{
    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _output;

    public AgentBlockRoundLiveTests(
        WorkflowsFixture<AgentBlockRoundLiveTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
        _output = outputHelper;
    }

    [Fact]
    public async Task AgainstALiveRuntime_ASecondLegRunsAsItsOwnIdAndKeepsLegOnesBookkeeping()
    {
        string baseUrl = AgentTestContexts.RequireLiveRuntime();
        (Block block, BlockExecutionContext context, _) = AgentTestContexts.ArrangeLiveAgentBlock(
            _testServiceProvider, baseUrl, "round-legs");

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        var props = Assert.IsType<AgentProperties>(block.Properties);
        AgentRunPass firstLeg = Assert.Single(props.ExecutionState!.RunPasses);
        Assert.EndsWith("_w_r1_1", firstLeg.ExecutionId, StringComparison.Ordinal);
        string firstLegExecutionId = firstLeg.ExecutionId;

        context.VerificationRound = 2;
        block.SetExecutionState(false, context.TextInputs);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        List<AgentRunPass> ledger = props.ExecutionState!.RunPasses;
        Assert.Equal(2, ledger.Count);
        AgentRunPass keptFirst = Assert.Single(ledger, pass => pass.Round == 1);
        Assert.Equal(firstLegExecutionId, keptFirst.ExecutionId);
        AgentRunPass secondLeg = Assert.Single(ledger, pass => pass.Round == 2);
        Assert.EndsWith("_w_r2_1", secondLeg.ExecutionId, StringComparison.Ordinal);
        Assert.NotEqual(keptFirst.ExecutionId, secondLeg.ExecutionId);

        // A frozen-registry replay of leg one would cost nothing; two real runs both bill.
        AgentRunPass[] legs = [keptFirst, secondLeg];
        foreach (AgentRunPass leg in legs)
        {
            _output.WriteLine(
                $"leg r{leg.Round}: id={leg.ExecutionId} status={leg.Status} cost={leg.EstimatedCostUsd}");
            Assert.Equal(nameof(AgentRunStatus.Succeeded), leg.Status);
            Assert.True(leg.EstimatedCostUsd > 0m);
        }

        // The durability half: each leg keeps its own attempt-output row, and the settled
        // response reflects the latest settled attempt instead of leg one's.
        Assert.Equal(2, block.AgentRunOutputs.Count);
        AgentRunOutput firstAttempt = Assert.Single(block.AgentRunOutputs, row => row.Round == 1);
        Assert.Equal(firstLegExecutionId, firstAttempt.RunExecutionId);
        Assert.False(string.IsNullOrEmpty(firstAttempt.Output));
        AgentRunOutput secondAttempt = Assert.Single(block.AgentRunOutputs, row => row.Round == 2);
        Assert.Equal(secondLeg.ExecutionId, secondAttempt.RunExecutionId);
        Assert.False(string.IsNullOrEmpty(secondAttempt.Output));

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(secondAttempt.Output, block.BlockResponse.RawResponse);
    }
}
