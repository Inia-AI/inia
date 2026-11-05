using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

public class Json2XlsxPropertiesExecutionStateTests
{
    [Fact]
    public void Constructor_CopiesAllFieldsFromSource()
    {
        // Arrange
        var props = new Json2XlsxProperties
        {
            JsonInputTemplate = "json {tag}",
            OutputFileName = "file {tag}",
            ConversionType = "conv {tag}",
            MappingJson = "map {tag}",
            CreatesDocumentClass = "doc {tag}"
        };

        // Act
        var state = new Json2XlsxPropertiesExecutionState(props);

        // Assert
        Assert.Equal("json {tag}", state.JsonInput);
        Assert.Equal("file {tag}", state.OutputFileName);
        Assert.Equal("conv {tag}", state.ConversionType);
        Assert.Equal("map {tag}", state.MappingJson);
        Assert.Equal("doc {tag}", state.CreatesDocumentClass);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInAllFields()
    {
        // Arrange
        var props = new Json2XlsxProperties
        {
            JsonInputTemplate = "prefix {tag} suffix",
            OutputFileName = "prefix {tag} suffix",
            ConversionType = "prefix {tag} suffix",
            MappingJson = "prefix {tag} suffix",
            CreatesDocumentClass = "prefix {tag} suffix"
        };
        var state = new Json2XlsxPropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.Equal("prefix resolved suffix", state.JsonInput);
        Assert.Equal("prefix resolved suffix", state.OutputFileName);
        Assert.Equal("prefix resolved suffix", state.ConversionType);
        Assert.Equal("prefix resolved suffix", state.MappingJson);
        Assert.Equal("prefix resolved suffix", state.CreatesDocumentClass);
    }

    [Fact]
    public void ReplaceAllPlaceholders_NullFields_DoesNotThrow()
    {
        // Arrange
        var props = new Json2XlsxProperties();
        var state = new Json2XlsxPropertiesExecutionState(props);

        // Act & Assert — should not throw
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });
        Assert.Null(state.JsonInput);
        Assert.Null(state.OutputFileName);
    }

    [Fact]
    public void SetExecutionState_OnProperties_CreatesAndResolvesState()
    {
        // Arrange
        var props = new Json2XlsxProperties { JsonInputTemplate = "prefix {tag} suffix" };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.JsonInput);
    }

    [Fact]
    public void UpdateExecutionState_WhenNull_CreatesState()
    {
        // Arrange
        var props = new Json2XlsxProperties { JsonInputTemplate = "prefix {tag} suffix" };
        Assert.Null(props.ExecutionState);

        // Act
        props.UpdateExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.JsonInput);
    }

    [Fact]
    public void Constructor_CopiesInputDocumentClass()
    {
        var props = new Json2XlsxProperties { InputDocumentClass = "doc {tag}" };

        var state = new Json2XlsxPropertiesExecutionState(props);

        Assert.Equal("doc {tag}", state.InputDocumentClass);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ResolvesInputDocumentClass()
    {
        var props = new Json2XlsxProperties { InputDocumentClass = "prefix {tag} suffix" };
        var state = new Json2XlsxPropertiesExecutionState(props);

        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        Assert.Equal("prefix resolved suffix", state.InputDocumentClass);
    }
}
