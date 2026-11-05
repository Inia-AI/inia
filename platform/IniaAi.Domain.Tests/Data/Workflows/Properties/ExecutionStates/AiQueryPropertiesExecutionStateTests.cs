using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

public class AiQueryPropertiesExecutionStateTests
{
    [Fact]
    public void Constructor_CopiesAllFieldsFromSource()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "user {tag}",
            SystemPromptTemplate = "system {tag}",
            TextToCache = "cache {tag}",
            UserPromptTemplatePreview = "preview {tag}",
            ModelIdOverride = "model {tag}",
            ConditionalTagTemplate = "conditional {tag}"
        };

        // Act
        var state = new AiQueryPropertiesExecutionState(props);

        // Assert
        Assert.Equal("user {tag}", state.UserPrompt);
        Assert.Equal("system {tag}", state.SystemPrompt);
        Assert.Equal("cache {tag}", state.TextToCache);
        Assert.Equal("preview {tag}", state.UserPromptPreview);
        Assert.Equal("model {tag}", state.ModelIdOverride);
        Assert.Equal("conditional {tag}", state.ConditionalTag);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInAllFields()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            UserPromptTemplate = "prefix {tag} suffix",
            SystemPromptTemplate = "prefix {tag} suffix",
            TextToCache = "prefix {tag} suffix",
            UserPromptTemplatePreview = "prefix {tag} suffix",
            ModelIdOverride = "prefix {tag} suffix",
            ConditionalTagTemplate = "prefix {tag} suffix"
        };
        var state = new AiQueryPropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.Equal("prefix resolved suffix", state.UserPrompt);
        Assert.Equal("prefix resolved suffix", state.SystemPrompt);
        Assert.Equal("prefix resolved suffix", state.TextToCache);
        Assert.Equal("prefix resolved suffix", state.UserPromptPreview);
        Assert.Equal("prefix resolved suffix", state.ModelIdOverride);
        Assert.Equal("prefix resolved suffix", state.ConditionalTag);
    }

    [Fact]
    public void ReplaceAllPlaceholders_NullFields_DoesNotThrow()
    {
        // Arrange
        var props = new AiQueryProperties();
        var state = new AiQueryPropertiesExecutionState(props);

        // Act & Assert — should not throw
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });
        Assert.Null(state.UserPrompt);
        Assert.Null(state.SystemPrompt);
        Assert.Null(state.TextToCache);
    }

    [Fact]
    public void SetExecutionState_OnProperties_CreatesAndResolvesState()
    {
        // Arrange
        var props = new AiQueryProperties { UserPromptTemplate = "prefix {tag} suffix" };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.UserPrompt);
    }

    [Fact]
    public void UpdateExecutionState_WhenNull_CreatesState()
    {
        // Arrange
        var props = new AiQueryProperties { UserPromptTemplate = "prefix {tag} suffix" };
        Assert.Null(props.ExecutionState);

        // Act
        props.UpdateExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.UserPrompt);
    }
}
