using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class BuildTopicsPropertiesTests
{
    [Fact]
    public void HasPlaceholder_ContainsInTopicsModelTemplate_ReturnsTrue()
    {
        // Arrange
        var props = new BuildTopicsProperties
        {
            TopicsModelTemplate = "Topics for {document}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{document}"));
    }

    [Fact]
    public void HasPlaceholder_DoesNotContain_ReturnsFalse()
    {
        // Arrange
        var props = new BuildTopicsProperties
        {
            TopicsModelTemplate = "Static topics model",
        };

        // Act & Assert
        Assert.False(props.HasPlaceholder("{missing}"));
    }

    [Fact]
    public void HasPlaceholder_NullProperties_ReturnsFalse()
    {
        // Arrange
        var props = new BuildTopicsProperties();

        // Act & Assert
        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsAllPlaceholders()
    {
        // Arrange
        var props = new BuildTopicsProperties
        {
            TopicsModelTemplate = "Topics for {document} with {context}",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Contains("document", result);
        Assert.Contains("context", result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty_WhenNoPlaceholders()
    {
        // Arrange
        var props = new BuildTopicsProperties
        {
            TopicsModelTemplate = "Static template",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReplaceAll_ReplacesInTopicsModelTemplate()
    {
        // Arrange
        var props = new BuildTopicsProperties
        {
            TopicsModelTemplate = "Original Topics Model",
        };

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Replaced Topics Model", props.TopicsModelTemplate);
    }

    [Fact]
    public void ReplaceAll_NullProperties_DoesNotThrow()
    {
        // Arrange
        var props = new BuildTopicsProperties();

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act & Assert (should not throw)
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);
    }
}
