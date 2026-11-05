using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

public class BuildTopicsPropertiesExecutionStateTests
{
    [Fact]
    public void Constructor_CopiesAllFieldsFromSource()
    {
        // Arrange
        var props = new BuildTopicsProperties { TopicsModelTemplate = "prefix {tag} suffix" };

        // Act
        var state = new BuildTopicsPropertiesExecutionState(props);

        // Assert
        Assert.Equal("prefix {tag} suffix", state.TopicsModel);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInAllFields()
    {
        // Arrange
        var props = new BuildTopicsProperties { TopicsModelTemplate = "prefix {tag} suffix" };
        var state = new BuildTopicsPropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.Equal("prefix resolved suffix", state.TopicsModel);
    }

    [Fact]
    public void ReplaceAllPlaceholders_NullFields_DoesNotThrow()
    {
        // Arrange
        var props = new BuildTopicsProperties();
        var state = new BuildTopicsPropertiesExecutionState(props);

        // Act & Assert — should not throw
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });
        Assert.Null(state.TopicsModel);
    }

    [Fact]
    public void SetExecutionState_OnProperties_CreatesAndResolvesState()
    {
        // Arrange
        var props = new BuildTopicsProperties { TopicsModelTemplate = "prefix {tag} suffix" };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.TopicsModel);
    }

    [Fact]
    public void UpdateExecutionState_WhenNull_CreatesState()
    {
        // Arrange
        var props = new BuildTopicsProperties { TopicsModelTemplate = "prefix {tag} suffix" };
        Assert.Null(props.ExecutionState);

        // Act
        props.UpdateExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.TopicsModel);
    }
}
