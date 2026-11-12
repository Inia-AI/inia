using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunIdsTests
{
    [Fact]
    public void For_ComposesTheCanonicalShape()
    {
        string executionId = AgentRunIds.For("wf-42", "block.7", "writer", 1, 1);

        Assert.Equal("wf-42_block.7_writer_r1_1", executionId);
        Assert.True(AgentRunIds.IsValid(executionId));
    }

    [Fact]
    public void For_ReplacesIllegalCharactersWithDashes()
    {
        string executionId = AgentRunIds.For("wf 42/α", "block:7", "wri ter", 1, 2);

        Assert.Equal("wf-42--_block-7_wri-ter_r1_2", executionId);
        Assert.True(AgentRunIds.IsValid(executionId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void For_EmptyComponent_ThrowsBeforeAnyHttpCouldHappen(string emptyComponent)
    {
        _ = Assert.Throws<ArgumentException>(() => AgentRunIds.For(emptyComponent, "block", "writer", 1, 1));
        _ = Assert.Throws<ArgumentException>(() => AgentRunIds.For("wf", emptyComponent, "writer", 1, 1));
        _ = Assert.Throws<ArgumentException>(() => AgentRunIds.For("wf", "block", emptyComponent, 1, 1));
    }

    [Fact]
    public void For_ComposedIdOverTheLimit_ThrowsInsteadOfTruncating()
    {
        string longComponent = new('a', 128);

        _ = Assert.Throws<ArgumentException>(() => AgentRunIds.For(longComponent, "block", "writer", 1, 1));
    }

    [Fact]
    public void For_LegsOfTheSameBlock_GetDistinctIds()
    {
        HashSet<string> ids =
        [
            AgentRunIds.For("wf", "block", "w", 1, 1),
            AgentRunIds.For("wf", "block", "w", 1, 2),
            AgentRunIds.For("wf", "block", "w", 2, 1),
            AgentRunIds.For("wf", "block", "w", 2, 2),
        ];

        Assert.Equal(4, ids.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void For_RoundBelowOne_ThrowsInsteadOfMintingAnUnkeyedId(int round)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => AgentRunIds.For("wf", "block", "w", round, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void For_PassBelowOne_ThrowsInsteadOfMintingAnUnkeyedId(int pass)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => AgentRunIds.For("wf", "block", "w", 1, pass));
    }

    [Theory]
    [InlineData("exec-1")]
    [InlineData("wf_block_writer_1")]
    [InlineData("A.b-C_9")]
    public void IsValid_AcceptsTheSidecarPattern(string executionId)
    {
        Assert.True(AgentRunIds.IsValid(executionId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("exec 1")]
    [InlineData("exec/1")]
    [InlineData("exec?x=1")]
    [InlineData("exec#1")]
    [InlineData("exec-1\n")]
    [InlineData(null)]
    public void IsValid_RejectsAnythingElse(string? executionId)
    {
        Assert.False(AgentRunIds.IsValid(executionId));
    }

    [Fact]
    public void IsValid_RejectsIdsOverTheLengthLimit()
    {
        Assert.True(AgentRunIds.IsValid(new string('a', 128)));
        Assert.False(AgentRunIds.IsValid(new string('a', 129)));
    }
}
