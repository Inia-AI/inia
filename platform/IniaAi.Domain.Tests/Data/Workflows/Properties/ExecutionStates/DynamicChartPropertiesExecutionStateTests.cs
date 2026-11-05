using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

public class DynamicChartPropertiesExecutionStateTests
{
    [Fact]
    public void Constructor_CopiesAllFieldsFromSource()
    {
        // Arrange
        var props = new DynamicChartProperties
        {
            DataSourceJson = "data {tag}",
            ChartType = "type {tag}",
            ChartTitle = "title {tag}"
        };

        // Act
        var state = new DynamicChartPropertiesExecutionState(props);

        // Assert
        Assert.Equal("data {tag}", state.DataSourceJson);
        Assert.Equal("type {tag}", state.ChartType);
        Assert.Equal("title {tag}", state.ChartTitle);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInAllFields()
    {
        // Arrange
        var props = new DynamicChartProperties
        {
            DataSourceJson = "prefix {tag} suffix",
            ChartType = "prefix {tag} suffix",
            XAxisField = "prefix {tag} suffix",
            YAxisField = "prefix {tag} suffix",
            SeriesGroupField = "prefix {tag} suffix",
            ChartTitle = "prefix {tag} suffix",
            XAxisLabel = "prefix {tag} suffix",
            YAxisLabel = "prefix {tag} suffix",
            ColorScheme = "prefix {tag} suffix",
            ChartHeight = "prefix {tag} suffix",
            ChartWidth = "prefix {tag} suffix",
            DataFilters = "prefix {tag} suffix",
            Stacking = "prefix {tag} suffix"
        };
        var state = new DynamicChartPropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.Equal("prefix resolved suffix", state.DataSourceJson);
        Assert.Equal("prefix resolved suffix", state.ChartType);
        Assert.Equal("prefix resolved suffix", state.XAxisField);
        Assert.Equal("prefix resolved suffix", state.YAxisField);
        Assert.Equal("prefix resolved suffix", state.SeriesGroupField);
        Assert.Equal("prefix resolved suffix", state.ChartTitle);
        Assert.Equal("prefix resolved suffix", state.XAxisLabel);
        Assert.Equal("prefix resolved suffix", state.YAxisLabel);
        Assert.Equal("prefix resolved suffix", state.ColorScheme);
        Assert.Equal("prefix resolved suffix", state.ChartHeight);
        Assert.Equal("prefix resolved suffix", state.ChartWidth);
        Assert.Equal("prefix resolved suffix", state.DataFilters);
        Assert.Equal("prefix resolved suffix", state.Stacking);
    }

    [Fact]
    public void ReplaceAllPlaceholders_NullFields_DoesNotThrow()
    {
        // Arrange — DataSourceJson is nullable, other fields have non-null defaults
        var props = new DynamicChartProperties();
        var state = new DynamicChartPropertiesExecutionState(props);

        // Act & Assert — should not throw
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });
        Assert.Null(state.DataSourceJson);
    }

    [Fact]
    public void SetExecutionState_OnProperties_CreatesAndResolvesState()
    {
        // Arrange
        var props = new DynamicChartProperties { DataSourceJson = "prefix {tag} suffix" };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.DataSourceJson);
    }

    [Fact]
    public void UpdateExecutionState_WhenNull_CreatesState()
    {
        // Arrange
        var props = new DynamicChartProperties { DataSourceJson = "prefix {tag} suffix" };
        Assert.Null(props.ExecutionState);

        // Act
        props.UpdateExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.DataSourceJson);
    }
}
