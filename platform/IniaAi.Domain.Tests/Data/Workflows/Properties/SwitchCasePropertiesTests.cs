using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class SwitchCasePropertiesTests
{
    [Fact]
    public void HasPlaceholder_ContainsInCaseValue_ReturnsTrue()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            Cases = new Dictionary<string, string>
            {
                ["key1"] = "value with {placeholder}",
            },
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{placeholder}"));
    }

    [Fact]
    public void HasPlaceholder_ContainsInDefaultValue_ReturnsTrue()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            DefaultValue = "default {value}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{value}"));
    }

    [Fact]
    public void HasPlaceholder_ContainsInInputTemplate_ReturnsTrue()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            InputTemplate = "input {data}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{data}"));
    }

    [Fact]
    public void HasPlaceholder_DoesNotContain_ReturnsFalse()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            Cases = new Dictionary<string, string>
            {
                ["key1"] = "static value",
            },
            DefaultValue = "static default",
            InputTemplate = "static input",
        };

        // Act & Assert
        Assert.False(props.HasPlaceholder("{missing}"));
    }

    [Fact]
    public void HasPlaceholder_NullProperties_ReturnsFalse()
    {
        // Arrange
        var props = new SwitchCaseProperties();

        // Act & Assert
        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsAllPlaceholders()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            Cases = new Dictionary<string, string>
            {
                ["key1"] = "case {a}",
                ["key2"] = "case {b}",
            },
            DefaultValue = "default {c}",
            InputTemplate = "input {d}",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Contains("a", result);
        Assert.Contains("b", result);
        Assert.Contains("c", result);
        Assert.Contains("d", result);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty_WhenNoPlaceholders()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            Cases = new Dictionary<string, string>
            {
                ["key1"] = "static",
            },
            DefaultValue = "static",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReplaceAll_ReplacesInAllTemplateProperties()
    {
        // Arrange
        var props = new SwitchCaseProperties
        {
            Cases = new Dictionary<string, string>
            {
                ["key1"] = "Original case 1",
                ["key2"] = "Original case 2",
            },
            DefaultValue = "Original default",
            InputTemplate = "Original input",
        };

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Replaced case 1", props.Cases["key1"]);
        Assert.Equal("Replaced case 2", props.Cases["key2"]);
        Assert.Equal("Replaced default", props.DefaultValue);
        Assert.Equal("Replaced input", props.InputTemplate);
    }

    [Fact]
    public void ReplaceAll_EmptyCases_DoesNotThrow()
    {
        // Arrange
        var props = new SwitchCaseProperties();

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act & Assert (should not throw)
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);
    }
}
