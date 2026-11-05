using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

public class LoopStartPropertiesExecutionStateTests
{
    [Fact]
    public void Constructor_CopiesAllFieldsFromSource()
    {
        // Arrange
        var props = new LoopStartProperties
        {
            LoopedListTemplate = "prefix {tag} suffix",
            ListTitle = "title {tag} end"
        };

        // Act
        var state = new LoopStartPropertiesExecutionState(props);

        // Assert
        Assert.Equal("prefix {tag} suffix", state.LoopedList);
        Assert.Equal("title {tag} end", state.ListTitle);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInAllFields()
    {
        // Arrange
        var props = new LoopStartProperties
        {
            LoopedListTemplate = "prefix {tag} suffix",
            ListTitle = "title {tag} end"
        };
        var state = new LoopStartPropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.Equal("prefix resolved suffix", state.LoopedList);
        Assert.Equal("title resolved end", state.ListTitle);
    }

    [Fact]
    public void ReplaceAllPlaceholders_NullFields_DoesNotThrow()
    {
        // Arrange
        var props = new LoopStartProperties();
        var state = new LoopStartPropertiesExecutionState(props);

        // Act & Assert — should not throw
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });
        Assert.Null(state.LoopedList);
        Assert.Null(state.ListTitle);
    }

    [Fact]
    public void SetExecutionState_OnProperties_CreatesAndResolvesState()
    {
        // Arrange
        var props = new LoopStartProperties
        {
            LoopedListTemplate = "prefix {tag} suffix",
            ListTitle = "title {tag} end"
        };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.LoopedList);
        Assert.Equal("title resolved end", props.ExecutionState.ListTitle);
    }

    [Fact]
    public void UpdateExecutionState_WhenNull_CreatesState()
    {
        // Arrange
        var props = new LoopStartProperties
        {
            LoopedListTemplate = "prefix {tag} suffix",
            ListTitle = "title {tag} end"
        };
        Assert.Null(props.ExecutionState);

        // Act
        props.UpdateExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.LoopedList);
        Assert.Equal("title resolved end", props.ExecutionState.ListTitle);
    }
}
