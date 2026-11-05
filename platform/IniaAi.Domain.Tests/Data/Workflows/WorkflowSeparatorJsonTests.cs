using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Properties;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class WorkflowSeparatorJsonTests
{
    [Fact]
    public void RoundTrip_WithSeparatorBetweenSections_PreservesSeparator()
    {
        var root = new Workflow("root");
        _ = new Section("first", root, 0) { Title = "First" };
        _ = new Section("", root, 1)
        {
            Properties = new SectionProperties { IsSeparator = true },
        };
        _ = new Section("second", root, 2) { Title = "Second" };

        string json = root.ToJson(pretty: false);
        var roundTripped = Workflow.FromJson(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(3, roundTripped.Sections.Count);

        List<Section> ordered = [.. roundTripped.Sections.OrderBy(s => s.Order)];
        Assert.False(ordered[0].IsSeparator);
        Assert.Equal("First", ordered[0].Title);
        Assert.True(ordered[1].IsSeparator);
        Assert.False(ordered[2].IsSeparator);
        Assert.Equal("Second", ordered[2].Title);
    }

    [Fact]
    public void Serialize_WithoutSeparators_DoesNotEmitPropertiesKey()
    {
        var root = new Workflow("root");
        _ = new Section("only", root, 0) { Title = "Only" };

        string json = root.ToJson(pretty: false);

        Assert.DoesNotContain("Properties", json);
    }

    [Fact]
    public void Serialize_DefaultPropertiesInstance_DoesNotEmitPropertiesKey()
    {
        var root = new Workflow("root");
        _ = new Section("a", root, 0)
        {
            Properties = new SectionProperties { IsSeparator = false },
        };

        string json = root.ToJson(pretty: false);

        Assert.DoesNotContain("Properties", json);
        Assert.DoesNotContain("IsSeparator", json);
    }
}
