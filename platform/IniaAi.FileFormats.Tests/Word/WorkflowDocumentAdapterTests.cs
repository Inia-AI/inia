using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.FileFormats.Word;

namespace IniaAi.FileFormats.Tests.Word;

public class WorkflowDocumentAdapterTests
{
    [Fact]
    public void ToDocumentElement_RootMetadata_MapsTitleAnalystAndCounterparty()
    {
        var report = new WorkflowReport
        {
            Title = "Quarterly Review",
            AnalystName = "Jane Roe",
            Counterparty = new Counterparty { Name = "Acme Corp" }
        };

        var root = WorkflowDocumentAdapter.ToDocumentElement(report);

        Assert.Equal(DocumentElementType.Paragraph, root.Type);
        Assert.Equal(0, root.Order);
        Assert.Equal("Quarterly Review", root.Title);
        Assert.Equal("Jane Roe", root.Metadata["AnalystName"]);
        Assert.Equal("Acme Corp", root.Metadata["Counterparty"]);
    }

    [Fact]
    public void ToDocumentElement_SectionWithParagraphs_BuildsSectionWithParagraphChildren()
    {
        var report = new WorkflowReport { Title = "Report" };
        var section = new ReportSection { Title = "Introduction" };
        report.Add(section);
        section.Add(new ReportParagraph
        {
            Kind = ReportParagraphKind.Paragraph,
            Content = "Body prose."
        });

        var root = WorkflowDocumentAdapter.ToDocumentElement(report);

        IDocumentElement sectionElement = Assert.Single(root.Children);
        Assert.Equal(DocumentElementType.Section, sectionElement.Type);
        Assert.Equal("Introduction", sectionElement.Title);

        IDocumentElement paragraphElement = Assert.Single(sectionElement.Children);
        Assert.Equal(DocumentElementType.Paragraph, paragraphElement.Type);
        Assert.False(string.IsNullOrEmpty(paragraphElement.Content));
    }

    [Fact]
    public void ToDocumentElement_AVerificationMarkerParagraph_KeepsItsSubtitleAndContent()
    {
        var report = new WorkflowReport { Title = "Report" };
        var section = new ReportSection { Title = "Financials" };
        report.Add(section);
        section.Add(new ReportParagraph
        {
            Kind = ReportParagraphKind.Paragraph,
            Title = "Verifier",
            Subtitle = "Unverified — the verifier could not confirm this work",
            Content = "Criteria still failing:\n- **Totals recompute** (review round 2) — off by 2",
        });
        section.Add(new ReportParagraph
        {
            Kind = ReportParagraphKind.Paragraph,
            Title = "Income",
            Content = "Revenue 42 with no annotation markup left in it.",
        });

        var root = WorkflowDocumentAdapter.ToDocumentElement(report);

        IDocumentElement sectionElement = Assert.Single(root.Children);
        IDocumentElement marker = sectionElement.Children[0];
        Assert.Equal("Unverified — the verifier could not confirm this work", marker.Subtitle);
        Assert.Contains("Totals recompute", marker.Content);
        IDocumentElement released = sectionElement.Children[1];
        Assert.Contains("Revenue 42 with no annotation markup left in it", released.Content);
    }

    [Fact]
    public void ToDocumentElement_SectionWithoutParagraphs_IsDropped()
    {
        var report = new WorkflowReport { Title = "Report" };
        report.Add(new ReportSection { Title = "Empty Section" });

        var root = WorkflowDocumentAdapter.ToDocumentElement(report);

        Assert.Empty(root.Children);
    }

    [Fact]
    public void ToDocumentElement_DynamicChartParagraph_RoutesChartPayloadToOutputData()
    {
        var report = new WorkflowReport { Title = "Report" };
        var section = new ReportSection { Title = "Charts" };
        report.Add(section);
        var chart = new ChartOutput { Name = "Revenue" };
        section.Add(new ReportParagraph
        {
            Kind = ReportParagraphKind.DynamicChart,
            ChartPayload = chart
        });

        var root = WorkflowDocumentAdapter.ToDocumentElement(report);

        IDocumentElement leaf = Assert.Single(root.Children[0].Children);
        Assert.Equal(DocumentElementType.Chart, leaf.Type);
        Assert.Same(chart, Assert.Single(leaf.OutputData));
    }

    [Fact]
    public void ToDocumentElement_TableParagraph_RendersFromContentNotTablePayload()
    {
        var report = new WorkflowReport { Title = "Report" };
        var section = new ReportSection { Title = "Tables" };
        report.Add(section);
        section.Add(new ReportParagraph
        {
            Kind = ReportParagraphKind.Table,
            Content = "<table><tr><td>A</td></tr></table>",
            TablePayload = new TableOutput { Name = "Metrics" }
        });

        var root = WorkflowDocumentAdapter.ToDocumentElement(report);

        IDocumentElement leaf = Assert.Single(root.Children[0].Children);
        Assert.Equal(DocumentElementType.Table, leaf.Type);
        Assert.False(string.IsNullOrEmpty(leaf.Content));
        Assert.Empty(leaf.OutputData);
    }

    [Fact]
    public void ToDocumentElement_SectionLayout_RoutesGridLayoutToSectionOutputData()
    {
        var report = new WorkflowReport { Title = "Report" };
        var layout = new GridLayoutOutput { Mode = "grid", Columns = 2 };
        var section = new ReportSection { Title = "Grid", Layout = layout };
        report.Add(section);
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = "cell" });

        var root = WorkflowDocumentAdapter.ToDocumentElement(report);

        IDocumentElement sectionElement = Assert.Single(root.Children);
        Assert.Same(layout, Assert.Single(sectionElement.OutputData));
    }

    [Fact]
    public void ToDocumentElement_DividerParagraph_MapsToDivider()
    {
        var report = new WorkflowReport { Title = "Report" };
        var section = new ReportSection { Title = "Section" };
        report.Add(section);
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Divider });

        var root = WorkflowDocumentAdapter.ToDocumentElement(report);

        IDocumentElement leaf = Assert.Single(root.Children[0].Children);
        Assert.Equal(DocumentElementType.Divider, leaf.Type);
    }

    [Fact]
    public void ToDocumentElement_ParagraphsEmittedInOrder()
    {
        var report = new WorkflowReport { Title = "Report" };
        var section = new ReportSection { Title = "Section" };
        report.Add(section);
        var first = new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Title = "A" };
        var second = new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Title = "B" };
        var third = new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Title = "C" };
        section.Add(first);
        section.Add(second);
        section.Add(third);
        first.Order = 2;
        second.Order = 0;
        third.Order = 1;

        var root = WorkflowDocumentAdapter.ToDocumentElement(report);

        IReadOnlyList<IDocumentElement> leaves = root.Children[0].Children;
        Assert.Equal("B", leaves[0].Title);
        Assert.Equal("C", leaves[1].Title);
        Assert.Equal("A", leaves[2].Title);
    }

    [Fact]
    public void ToDocumentElement_SectionLeftOutOfWordExport_IsAbsentAndTheOthersKeepTheirOrder()
    {
        var report = new WorkflowReport { Title = "Report" };
        report.Add(SectionWithParagraph("Summary", "Summary prose."));
        report.Add(SectionWithParagraph("Debug trace", "Intermediate reasoning.", excludeFromWordExport: true));
        report.Add(SectionWithParagraph("Conclusion", "Conclusion prose."));

        var root = WorkflowDocumentAdapter.ToDocumentElement(report);

        Assert.Equal(["Summary", "Conclusion"], root.Children.Select(c => c.Title));
    }

    private static ReportSection SectionWithParagraph(string title, string content, bool excludeFromWordExport = false)
    {
        var section = new ReportSection { Title = title, ExcludeFromWordExport = excludeFromWordExport };
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = content });
        return section;
    }
}
