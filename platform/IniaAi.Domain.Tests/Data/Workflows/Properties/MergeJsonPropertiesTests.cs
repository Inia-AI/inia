using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class MergeJsonPropertiesTests
{
    [Fact]
    public void Defaults_AreBackwardsCompatible()
    {
        var props = new MergeJsonProperties();

        Assert.Empty(props.JsonInputs);
        Assert.Empty(props.InputExtractions);
    }

    [Fact]
    public void Clone_DeepCopiesInputExtractions()
    {
        var props = new MergeJsonProperties
        {
            JsonInputs = { "{block_0}", "{block_1}" },
            InputExtractions =
            {
                new MergeJsonInputExtraction { Mode = JsonExtractionMode.Single },
                new MergeJsonInputExtraction { Mode = JsonExtractionMode.XmlTagPattern, Pattern = "page*" },
            },
        };

        var clone = (MergeJsonProperties)props.Clone();

        Assert.Equal(2, clone.InputExtractions.Count);
        Assert.Equal(JsonExtractionMode.Single, clone.InputExtractions[0].Mode);
        Assert.Equal(JsonExtractionMode.XmlTagPattern, clone.InputExtractions[1].Mode);
        Assert.Equal("page*", clone.InputExtractions[1].Pattern);

        // Mutating the clone must not affect source — it's a deep copy.
        clone.InputExtractions[1].Pattern = "changed";
        Assert.Equal("page*", props.InputExtractions[1].Pattern);
    }

    [Fact]
    public void HasPlaceholder_WhenNoInputs_ReturnsFalse()
    {
        var props = new MergeJsonProperties();

        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void HasPlaceholder_WhenValueContainsPlaceholder_ReturnsTrue()
    {
        var props = new MergeJsonProperties
        {
            JsonInputs = { "{block_0}", "{\"a\":1}" },
        };

        Assert.True(props.HasPlaceholder("{block_0}"));
        Assert.False(props.HasPlaceholder("{missing}"));
    }

    [Fact]
    public void GetAllPlaceholders_WhenNoInputs_ReturnsEmpty()
    {
        var props = new MergeJsonProperties();

        List<string> result = props.GetAllPlaceholders();

        Assert.Empty(result);
    }

    [Fact]
    public void GetAllPlaceholders_ExtractsFromAllItems()
    {
        var props = new MergeJsonProperties
        {
            JsonInputs = { "{block_0}", "{block_1}", "static" },
        };

        List<string> result = props.GetAllPlaceholders();

        Assert.Contains("block_0", result);
        Assert.Contains("block_1", result);
    }

    [Fact]
    public void ReplaceAll_ReplacesPlaceholdersInAllItems()
    {
        var props = new MergeJsonProperties
        {
            JsonInputs = { "{block_0}", "static-value", "{block_1}" },
        };

        props.ReplaceAll(new Dictionary<string, string?>
        {
            ["{block_0}"] = "\"hello\"",
            ["{block_1}"] = "\"world\"",
        });

        Assert.Equal("\"hello\"", props.JsonInputs[0]);
        Assert.Equal("static-value", props.JsonInputs[1]);
        Assert.Equal("\"world\"", props.JsonInputs[2]);
    }

    [Fact]
    public void ReplaceAll_EmptyInputs_DoesNotThrow()
    {
        var props = new MergeJsonProperties();

        props.ReplaceAll(new Dictionary<string, string?> { ["Original"] = "Replaced" });

        Assert.Empty(props.JsonInputs);
    }
}
