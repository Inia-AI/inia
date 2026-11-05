using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;

namespace IniaAi.Domain.Tests.Data.WorkflowExecutions;

public class WorkflowReportTests
{
    [Fact]
    public void Ctor_Default_HasUndefinedStateAndEmptySections()
    {
        var report = new WorkflowReport();

        Assert.Equal(WorkflowState.Undefined, report.WorkflowState);
        Assert.Empty(report.Sections);
        Assert.False(string.IsNullOrEmpty(report.Id));
    }

    [Fact]
    public void Sections_AddReportSection_LinksBack()
    {
        var report = new WorkflowReport();
        var section = new ReportSection
        {
            WorkflowReportId = report.Id,
            WorkflowReport = report,
            Title = "Section title",
        };

        report.Sections.Add(section);

        Assert.Same(report, section.WorkflowReport);
        Assert.Equal(report.Id, section.WorkflowReportId);
        _ = Assert.Single(report.Sections);
    }

    [Fact]
    public void ReportSection_Paragraphs_AddReportParagraph_LinksBack()
    {
        var report = new WorkflowReport();
        var section = new ReportSection
        {
            WorkflowReportId = report.Id,
            WorkflowReport = report,
        };
        report.Sections.Add(section);

        var paragraph = new ReportParagraph
        {
            SectionId = section.Id,
            Section = section,
            Kind = ReportParagraphKind.Paragraph,
            Content = "Hello",
        };
        section.Paragraphs.Add(paragraph);

        Assert.Same(section, paragraph.Section);
        Assert.Equal(section.Id, paragraph.SectionId);
        Assert.Equal(ReportParagraphKind.Paragraph, paragraph.Kind);
        _ = Assert.Single(section.Paragraphs);
    }

    [Fact]
    public void ReportParagraph_DynamicChartKind_AcceptsChartPayload()
    {
        var paragraph = new ReportParagraph
        {
            Kind = ReportParagraphKind.DynamicChart,
            ChartPayload = new ChartOutput { Name = "Trend" },
        };

        Assert.Equal(ReportParagraphKind.DynamicChart, paragraph.Kind);
        Assert.NotNull(paragraph.ChartPayload);
        Assert.Equal("Trend", paragraph.ChartPayload!.Name);
    }

    [Fact]
    public void ReportParagraph_TableKind_AcceptsTablePayload()
    {
        var paragraph = new ReportParagraph
        {
            Kind = ReportParagraphKind.Table,
            TablePayload = new TableOutput
            {
                TableName = "Revenue",
                TableData = [["2024", "100"], ["2025", "120"]],
            },
        };

        Assert.Equal(ReportParagraphKind.Table, paragraph.Kind);
        Assert.NotNull(paragraph.TablePayload);
        Assert.Equal("Revenue", paragraph.TablePayload!.TableName);
        Assert.Equal(2, paragraph.TablePayload.TableData.Count);
    }

    [Fact]
    public void WorkflowReport_Add_AssignsOrderAndLinks()
    {
        var report = new WorkflowReport();
        var first = new ReportSection { Title = "first" };
        var second = new ReportSection { Title = "second" };

        report.Add(first);
        report.Add(second);

        Assert.Equal(0, first.Order);
        Assert.Equal(1, second.Order);
        Assert.Same(report, first.WorkflowReport);
        Assert.Same(report, second.WorkflowReport);
        Assert.Equal(report.Id, first.WorkflowReportId);
    }

    [Fact]
    public void ReportSection_Add_AssignsOrderAndLinks()
    {
        var section = new ReportSection();
        var first = new ReportParagraph { Content = "first" };
        var second = new ReportParagraph { Content = "second" };

        section.Add(first);
        section.Add(second);

        Assert.Equal(0, first.Order);
        Assert.Equal(1, second.Order);
        Assert.Same(section, first.Section);
        Assert.Same(section, second.Section);
        Assert.Equal(section.Id, first.SectionId);
    }

    [Fact]
    public void WorkflowReport_Remove_RemovesAndRenumbers()
    {
        var report = new WorkflowReport();
        var a = new ReportSection { Title = "a" };
        var b = new ReportSection { Title = "b" };
        var c = new ReportSection { Title = "c" };
        report.Add(a);
        report.Add(b);
        report.Add(c);

        report.Remove(b);

        Assert.Equal(2, report.Sections.Count);
        Assert.Equal(0, a.Order);
        Assert.Equal(1, c.Order);
    }

    [Fact]
    public void ReportSection_Remove_RemovesAndRenumbers()
    {
        var section = new ReportSection();
        var a = new ReportParagraph { Content = "a" };
        var b = new ReportParagraph { Content = "b" };
        var c = new ReportParagraph { Content = "c" };
        section.Add(a);
        section.Add(b);
        section.Add(c);

        section.Remove(b);

        Assert.Equal(2, section.Paragraphs.Count);
        Assert.Equal(0, a.Order);
        Assert.Equal(1, c.Order);
    }

    [Fact]
    public void ReportSection_FindParagraphByReplacementTag_ReturnsMatchOrNull()
    {
        var section = new ReportSection();
        var a = new ReportParagraph { ReplacementTag = "tag_a" };
        var b = new ReportParagraph { ReplacementTag = "tag_b" };
        section.Add(a);
        section.Add(b);

        Assert.Same(a, section.FindParagraphByReplacementTag("tag_a"));
        Assert.Null(section.FindParagraphByReplacementTag("missing"));
    }

    [Fact]
    public void WorkflowReport_Clear_EmptiesSections()
    {
        var report = new WorkflowReport();
        report.Add(new ReportSection());
        report.Add(new ReportSection());

        report.Clear();

        Assert.Empty(report.Sections);
    }
}
