using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class LoopItemPlaceholderExpansionTests
{
    private const string BODY_TEMPLATE = """
        Fund: {items}
        Style: {items.ReportingStyle}
        Currency: {items.ReportingStyle.Currency}
        """;

    [Fact]
    public void ProcessLoops_BareListTitle_IsBoundToItsIteration()
    {
        Section section = ExpandLoopSection();

        Assert.Contains("Fund: {items_0}", GetTemplate(section, "items_body_0"));
        Assert.Contains("Fund: {items_1}", GetTemplate(section, "items_body_1"));
    }

    [Fact]
    public void ProcessLoops_DottedListTitle_IsBoundToItsIteration()
    {
        Section section = ExpandLoopSection();

        Assert.Contains("Style: {items_0.ReportingStyle}", GetTemplate(section, "items_body_0"));
        Assert.Contains("Style: {items_1.ReportingStyle}", GetTemplate(section, "items_body_1"));
    }

    [Fact]
    public void ProcessLoops_MultiSegmentDottedListTitle_IsBoundToItsIteration()
    {
        Section section = ExpandLoopSection();

        Assert.Contains("Currency: {items_0.ReportingStyle.Currency}", GetTemplate(section, "items_body_0"));
        Assert.Contains("Currency: {items_1.ReportingStyle.Currency}", GetTemplate(section, "items_body_1"));
    }

    [Fact]
    public void ProcessLoops_DottedListTitle_LeavesNoUnboundPlaceholder()
    {
        Section section = ExpandLoopSection();

        Assert.DoesNotContain("{items.", GetTemplate(section, "items_body_0"));
        Assert.DoesNotContain("{items}", GetTemplate(section, "items_body_0"));
    }

    [Fact]
    public void ProcessLoops_LoopItemBlock_OutputsTheListItem()
    {
        Section section = ExpandLoopSection();

        Assert.Equal("alpha", GetTemplate(section, "items_0"));
        Assert.Equal("bravo", GetTemplate(section, "items_1"));
    }

    [Fact]
    public void ProcessLoops_DottedListTitleAfterLoop_IsLeftAlone()
    {
        // Outside the loop body there is no iteration to bind to, so the placeholder stays
        // as authored rather than silently binding to an arbitrary fund.
        Section section = ExpandLoopSection();

        Assert.Equal("After: {items.ReportingStyle}", GetTemplate(section, "after"));
    }

    private static string GetTemplate(Section section, string replacementTag)
    {
        Block block = section.Blocks.Single(b => b.ReplacementTag == replacementTag);

        return ((MergeProperties)block.Properties).TextTemplate ?? string.Empty;
    }

    private static Section ExpandLoopSection()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "s" };

        var loopStart = new Block(BlockTypes.LoopStart, section)
        {
            Properties = new LoopStartProperties
            {
                ListTitle = "items",
                LoopedListTemplate = "alpha\nbravo",
            },
        };
        loopStart.StableBlockId = "loopstart";

        var body = new Block(BlockTypes.Merge, section, "body")
        {
            Properties = new MergeProperties { TextTemplate = BODY_TEMPLATE },
        };
        Assert.NotNull(body);

        _ = new Block(BlockTypes.LoopEnd, section);

        var afterLoop = new Block(BlockTypes.Merge, section, "after")
        {
            Properties = new MergeProperties { TextTemplate = "After: {items.ReportingStyle}" },
        };
        Assert.NotNull(afterLoop);

        section.ProcessLoops([]);

        return section;
    }
}
