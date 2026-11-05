using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

public class SwitchCasePropertiesExecutionStateTests
{
    [Fact]
    public void Constructor_CopiesAllFieldsFromSource()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            Cases = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } },
            DefaultValue = "default {tag}",
            InputTemplate = "input {tag}"
        };

        // Act
        var state = new SwitchCasePropertiesExecutionState(props);

        // Assert
        Assert.Equal(2, state.Cases.Count);
        Assert.Equal("value1", state.Cases["key1"]);
        Assert.Equal("value2", state.Cases["key2"]);
        Assert.Equal("default {tag}", state.DefaultValue);
        Assert.Equal("input {tag}", state.Input);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInAllFields()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            Cases = new Dictionary<string, string>
            {
                { "caseA", "prefix {tag} suffix" },
                { "caseB", "prefix {tag} suffix" }
            },
            DefaultValue = "prefix {tag} suffix",
            InputTemplate = "prefix {tag} suffix"
        };
        var state = new SwitchCasePropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.Equal("prefix resolved suffix", state.Cases["caseA"]);
        Assert.Equal("prefix resolved suffix", state.Cases["caseB"]);
        Assert.Equal("prefix resolved suffix", state.DefaultValue);
        Assert.Equal("prefix resolved suffix", state.Input);
    }

    [Fact]
    public void ReplaceAllPlaceholders_NullFields_DoesNotThrow()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            Cases = [],
            DefaultValue = null,
            InputTemplate = null
        };
        var state = new SwitchCasePropertiesExecutionState(props);

        // Act & Assert — should not throw
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });
        Assert.Empty(state.Cases);
        Assert.Null(state.DefaultValue);
        Assert.Null(state.Input);
    }

    [Fact]
    public void SetExecutionState_OnProperties_CreatesAndResolvesState()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            Cases = new Dictionary<string, string> { { "k", "prefix {tag} suffix" } },
            DefaultValue = "prefix {tag} suffix",
            InputTemplate = "prefix {tag} suffix"
        };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.Cases["k"]);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.DefaultValue);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.Input);
    }

    [Fact]
    public void UpdateExecutionState_WhenNull_CreatesState()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            Cases = new Dictionary<string, string> { { "k", "prefix {tag} suffix" } },
            DefaultValue = "prefix {tag} suffix",
            InputTemplate = "prefix {tag} suffix"
        };
        Assert.Null(props.ExecutionState);

        // Act
        props.UpdateExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.Cases["k"]);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.DefaultValue);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.Input);
    }

    [Fact]
    public void Constructor_CopiesCases_AsDefensiveCopy()
    {
        // Arrange
        var sourceCases = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };
        var props = new SwitchCaseProperties { Cases = sourceCases };

        // Act
        var state = new SwitchCasePropertiesExecutionState(props);

        // Modify source after construction
        sourceCases["key1"] = "modified";
        sourceCases["key3"] = "new";

        // Assert — state should be unaffected
        Assert.Equal("value1", state.Cases["key1"]);
        Assert.Equal("value2", state.Cases["key2"]);
        Assert.Equal(2, state.Cases.Count);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesCaseValues_NotKeys()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            Cases = new Dictionary<string, string>
            {
                { "{tag}", "prefix {tag} suffix" },
                { "static", "prefix {tag} suffix" }
            }
        };
        var state = new SwitchCasePropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert — values replaced, keys untouched
        Assert.Equal("prefix resolved suffix", state.Cases["{tag}"]);
        Assert.Equal("prefix resolved suffix", state.Cases["static"]);
        Assert.True(state.Cases.ContainsKey("{tag}"));
    }
}
