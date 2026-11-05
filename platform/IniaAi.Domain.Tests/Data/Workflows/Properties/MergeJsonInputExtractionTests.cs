using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class MergeJsonInputExtractionTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var ext = new MergeJsonInputExtraction();

        Assert.Equal(JsonExtractionMode.Single, ext.Mode);
        Assert.Equal("*", ext.Pattern);
    }

    [Fact]
    public void Clone_CopiesScalarFields()
    {
        var original = new MergeJsonInputExtraction
        {
            Mode = JsonExtractionMode.Regex,
            Pattern = @"<page\d+>([\s\S]*?)</page\d+>",
        };

        MergeJsonInputExtraction clone = original.Clone();

        Assert.Equal(JsonExtractionMode.Regex, clone.Mode);
        Assert.Equal(@"<page\d+>([\s\S]*?)</page\d+>", clone.Pattern);
    }

    [Fact]
    public void Clone_IsIndependentOfSource()
    {
        var original = new MergeJsonInputExtraction
        {
            Mode = JsonExtractionMode.XmlTagPattern,
            Pattern = "page*",
        };

        MergeJsonInputExtraction clone = original.Clone();

        clone.Mode = JsonExtractionMode.Regex;
        clone.Pattern = "changed";

        Assert.Equal(JsonExtractionMode.XmlTagPattern, original.Mode);
        Assert.Equal("page*", original.Pattern);
    }
}
