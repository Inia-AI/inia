using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class CalculateFormulaPropertiesTests
{
    [Fact]
    public void HasPlaceholder_ContainsInFormulaTemplate_ReturnsTrue()
    {
        // Arrange
        var props = new CalculateFormulaProperties
        {
            FormulaTemplate = "{a} + {b}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{a}"));
    }

    [Fact]
    public void HasPlaceholder_DoesNotContain_ReturnsFalse()
    {
        // Arrange
        var props = new CalculateFormulaProperties
        {
            FormulaTemplate = "1 + 2",
        };

        // Act & Assert
        Assert.False(props.HasPlaceholder("{missing}"));
    }

    [Fact]
    public void HasPlaceholder_NullProperties_ReturnsFalse()
    {
        // Arrange
        var props = new CalculateFormulaProperties();

        // Act & Assert
        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsAllPlaceholders()
    {
        // Arrange
        var props = new CalculateFormulaProperties
        {
            FormulaTemplate = "{a} + {b} * {c}",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Contains("a", result);
        Assert.Contains("b", result);
        Assert.Contains("c", result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty_WhenNoPlaceholders()
    {
        // Arrange
        var props = new CalculateFormulaProperties
        {
            FormulaTemplate = "1 + 2",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReplaceAll_ReplacesInFormulaTemplate()
    {
        // Arrange
        var props = new CalculateFormulaProperties
        {
            FormulaTemplate = "Original Formula",
        };

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Replaced Formula", props.FormulaTemplate);
    }

    [Fact]
    public void ReplaceAll_NullProperties_DoesNotThrow()
    {
        // Arrange
        var props = new CalculateFormulaProperties();

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act & Assert (should not throw)
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);
    }
}
