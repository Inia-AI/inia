using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class SectionExtensionsTests
{
    [Fact]
    public void NonSeparator_FiltersOutSeparators()
    {
        var root = new Workflow("root");
        var first = new Section("first", root);
        var separator = new Section("", root)
        {
            Properties = new SectionProperties { IsSeparator = true },
        };
        var second = new Section("second", root);

        List<Section> result = [.. root.Sections.NonSeparator()];

        Assert.Equal(2, result.Count);
        Assert.Contains(first, result);
        Assert.Contains(second, result);
        Assert.DoesNotContain(separator, result);
    }

    [Fact]
    public void NonSeparator_AllSeparators_ReturnsEmpty()
    {
        var root = new Workflow("root");
        _ = new Section("", root)
        {
            Properties = new SectionProperties { IsSeparator = true },
        };
        _ = new Section("", root)
        {
            Properties = new SectionProperties { IsSeparator = true },
        };

        List<Section> result = [.. root.Sections.NonSeparator()];

        Assert.Empty(result);
    }

    [Fact]
    public void NonSeparator_NoSeparators_ReturnsAll()
    {
        var root = new Workflow("root");
        var a = new Section("a", root);
        var b = new Section("b", root);

        List<Section> result = [.. root.Sections.NonSeparator()];

        Assert.Equal(2, result.Count);
        Assert.Contains(a, result);
        Assert.Contains(b, result);
    }
}
