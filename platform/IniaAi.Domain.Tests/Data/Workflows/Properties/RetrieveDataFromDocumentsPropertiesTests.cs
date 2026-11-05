using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class RetrieveDataFromDocumentsPropertiesTests
{
    [Fact]
    public void HasPlaceholder_AlwaysReturnsFalse()
    {
        // Arrange
        var props = new RetrieveDataFromDocumentsProperties();

        // Act & Assert
        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty()
    {
        // Arrange
        var props = new RetrieveDataFromDocumentsProperties();

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReplaceAll_DoesNotThrow()
    {
        // Arrange
        var props = new RetrieveDataFromDocumentsProperties();

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act & Assert (should not throw)
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);
    }
}
