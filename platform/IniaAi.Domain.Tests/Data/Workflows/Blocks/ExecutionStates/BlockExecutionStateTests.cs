using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows.Blocks.ExecutionStates;

public class BlockExecutionStateTests
{
    [Fact]
    public void Error_RemovesNullCharacters()
    {
        BlockExecutionState state = CreateState();

        state.Error = "could not parse: val" + '\0' + "ue";

        Assert.Equal("could not parse: value", state.Error);
    }

    [Fact]
    public void Error_NullValue_StaysNull()
    {
        BlockExecutionState state = CreateState();

        state.Error = "some error";
        state.Error = null;

        Assert.Null(state.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void OutputTitle_Blank_IsNone(string blank)
    {
        BlockExecutionState state = CreateState();

        state.OutputTitle = blank;

        Assert.Null(state.OutputTitle);
    }

    [Fact]
    public void OutputTitle_WithText_IsKept()
    {
        BlockExecutionState state = CreateState();

        state.OutputTitle = "Leverage as of FY2025";

        Assert.Equal("Leverage as of FY2025", state.OutputTitle);
    }

    [Fact]
    public void MarkAsFailed_RemovesNullCharactersFromError()
    {
        BlockExecutionState state = CreateState();

        state.MarkAsFailed("upstream said: pen" + '\0' + "ding");

        Assert.Equal("upstream said: pending", state.Error);
    }

    private static BlockExecutionState CreateState()
    {
        var section = new Section("test", new Workflow("test"));
        var block = new Block(BlockTypes.AiQuery, section);
        return new BlockExecutionState(block);
    }
}
