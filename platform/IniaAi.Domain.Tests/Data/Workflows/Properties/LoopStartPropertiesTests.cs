using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class LoopStartPropertiesTests
{
    [Fact]
    public void HasPlaceholder_ContainsInLoopedListTemplate_ReturnsTrue()
    {
        // Arrange
        var props = new LoopStartProperties
        {
            LoopedListTemplate = "Loop {list}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{list}"));
    }

    [Fact]
    public void HasPlaceholder_DoesNotContain_ReturnsFalse()
    {
        // Arrange
        var props = new LoopStartProperties
        {
            LoopedListTemplate = "Loop something",
        };

        // Act & Assert
        Assert.False(props.HasPlaceholder("{missing}"));
    }

    [Fact]
    public void HasPlaceholder_NullProperties_ReturnsFalse()
    {
        // Arrange
        var props = new LoopStartProperties();

        // Act & Assert
        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsAllPlaceholders()
    {
        // Arrange
        var props = new LoopStartProperties
        {
            LoopedListTemplate = "Loop {item1} and {item2}",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Contains("item1", result);
        Assert.Contains("item2", result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty_WhenNoPlaceholders()
    {
        // Arrange
        var props = new LoopStartProperties
        {
            LoopedListTemplate = "Static list",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReplaceAll_ReplacesInLoopedListTemplate()
    {
        // Arrange
        var props = new LoopStartProperties
        {
            LoopedListTemplate = "Original Loop List",
        };

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Replaced Loop List", props.LoopedListTemplate);
    }

    [Fact]
    public void ReplaceAll_NullProperties_DoesNotThrow()
    {
        // Arrange
        var props = new LoopStartProperties();

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act & Assert (should not throw)
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);
    }
}
