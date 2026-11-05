using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class IntelligentSearchPropertiesTests
{
    [Fact]
    public void HasPlaceholder_ContainsInSearchTerm_ReturnsTrue()
    {
        // Arrange
        var props = new IntelligentSearchProperties
        {
            SearchTermTemplate = "Search {term}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{term}"));
    }

    [Fact]
    public void HasPlaceholder_DoesNotContain_ReturnsFalse()
    {
        // Arrange
        var props = new IntelligentSearchProperties
        {
            SearchTermTemplate = "Search something",
        };

        // Act & Assert
        Assert.False(props.HasPlaceholder("{missing}"));
    }

    [Fact]
    public void HasPlaceholder_NullProperties_ReturnsFalse()
    {
        // Arrange
        var props = new IntelligentSearchProperties();

        // Act & Assert
        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsAllPlaceholders()
    {
        // Arrange
        var props = new IntelligentSearchProperties
        {
            SearchTermTemplate = "Search {term} and {query}",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Contains("term", result);
        Assert.Contains("query", result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty_WhenNoPlaceholders()
    {
        // Arrange
        var props = new IntelligentSearchProperties
        {
            SearchTermTemplate = "Search something",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReplaceAll_ReplacesInSearchTermTemplate()
    {
        // Arrange
        var props = new IntelligentSearchProperties
        {
            SearchTermTemplate = "Original Search Term",
        };

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Replaced Search Term", props.SearchTermTemplate);
    }

    [Fact]
    public void ReplaceAll_NullProperties_DoesNotThrow()
    {
        // Arrange
        var props = new IntelligentSearchProperties();

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act & Assert (should not throw)
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public void RequiresDocumentClass_IsFalse()
    {
        Assert.False(new IntelligentSearchProperties().RequiresDocumentClass);
    }

    [Fact]
    public void RequiresDocumentClass_IsNotSerialized()
    {
        string newtonsoftJson = JsonConvert.SerializeObject(new IntelligentSearchProperties());
        string systemTextJson = System.Text.Json.JsonSerializer.Serialize(new IntelligentSearchProperties());

        Assert.DoesNotContain(nameof(IntelligentSearchProperties.RequiresDocumentClass), newtonsoftJson);
        Assert.DoesNotContain(nameof(IntelligentSearchProperties.RequiresDocumentClass), systemTextJson);
    }
}
