using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class DynamicChartPropertiesTests
{
    [Fact]
    public void HasPlaceholder_ContainsInDataSourceJson_ReturnsTrue()
    {
        // Arrange
        var props = new DynamicChartProperties
        {
            DataSourceJson = "{chartData}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{chartData}"));
    }

    [Fact]
    public void HasPlaceholder_DoesNotContain_ReturnsFalse()
    {
        // Arrange
        var props = new DynamicChartProperties
        {
            DataSourceJson = "[{\"x\": 1, \"y\": 2}]",
        };

        // Act & Assert
        Assert.False(props.HasPlaceholder("{missing}"));
    }

    [Fact]
    public void HasPlaceholder_NullProperties_ReturnsFalse()
    {
        // Arrange
        var props = new DynamicChartProperties();

        // Act & Assert
        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsAllPlaceholders()
    {
        // Arrange
        var props = new DynamicChartProperties
        {
            DataSourceJson = "{chartData}",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Contains("chartData", result);
        Assert.Single(result);
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty_WhenNoPlaceholders()
    {
        // Arrange
        var props = new DynamicChartProperties
        {
            DataSourceJson = "[{\"x\": 1}]",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReplaceAll_ReplacesInDataSourceJson()
    {
        // Arrange
        var props = new DynamicChartProperties
        {
            DataSourceJson = "Original Data",
        };

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Replaced Data", props.DataSourceJson);
    }

    [Fact]
    public void ReplaceAll_NullProperties_DoesNotThrow()
    {
        // Arrange
        var props = new DynamicChartProperties();

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act & Assert (should not throw)
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);
    }
}
