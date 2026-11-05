using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;

namespace IniaAi.FileFormats.ContractTests.Reports;

/// <summary>A credit review with a heading, a list, a citation, a table, a chart and a section left out of exports.</summary>
public static class CreditReviewReportFixture
{
    public const string EXCLUDED_SECTION_TITLE = "Debug trace";
    public const string ANALYST = "Jane Analyst";
    public const string LINKS_AS_MARKDOWN = "Run the script, open the page or see [the method](https://example.com/method), [the desk](mailto:desk@example.com) and [the summary](/reports/42).";

    private const string EXCLUDED_TEXT = "Intermediate reasoning for the workflow engineer.";
    private const string LINKS_SOURCE = "Run [the script](javascript:alert(1)), open [the page](JaVaScRiPt:alert(2)) or see [the method](https://example.com/method), [the desk](mailto:desk@example.com) and [the summary](/reports/42).";

    public static WorkflowReport WithLinks()
    {
        var report = new WorkflowReport { Title = "Links" };
        var section = new ReportSection { Title = "Sources" };
        report.Add(section);
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = LINKS_SOURCE });
        return report;
    }

    public static WorkflowReport Build()
    {
        var report = new WorkflowReport
        {
            Title = "Acme Holdings — Credit Review",
            AnalystName = ANALYST,
            Counterparty = new Counterparty { Name = "Acme Holdings" },
        };

        var summary = new ReportSection { Title = "Executive Summary" };
        report.Add(summary);
        summary.Add(new ReportParagraph
        {
            Kind = ReportParagraphKind.Paragraph,
            Title = "Summary block",
            Content = """
                # Overview

                Net leverage is **<fact id="c0">2.1x</fact>**, inside the *3.5x* covenant.<fact_source type="document" page-id="p-4" label="Annual report - page 4"><cited-text>Net leverage 2.1x</cited-text></fact_source> See [the method](https://example.com/method).

                - Revenue grew 18%
                - Margin expanded
                """,
        });

        var financials = new ReportSection { Title = "Financials" };
        report.Add(financials);
        financials.Add(new ReportParagraph
        {
            Kind = ReportParagraphKind.Table,
            Content = """<table style="border-collapse:collapse"><thead><tr><th>Metric</th><th>FY2024</th><th>FY2025</th></tr></thead><tbody><tr><td>Revenue</td><td>264</td><td>312</td></tr><tr><td>EBITDA</td><td>52.1</td><td>69.9</td></tr></tbody></table>""",
        });
        financials.Add(new ReportParagraph
        {
            Kind = ReportParagraphKind.DynamicChart,
            Title = "Revenue chart",
            Content = "Chart: Revenue trend",
            ChartPayload = new ChartOutput
            {
                Name = "Revenue trend",
                ChartConfig = new ChartConfiguration { Type = "column", Title = "Revenue trend" },
                ChartData =
                [
                    new ChartDataPoint { X = "FY2024", Y = 264, Series = "Revenue" },
                    new ChartDataPoint { X = "FY2025", Y = 312, Series = "Revenue" },
                    new ChartDataPoint { X = "FY2024", Y = 52.1, Series = "EBITDA" },
                    new ChartDataPoint { X = "FY2025", Y = 69.9, Series = "EBITDA" },
                ],
            },
        });

        var debug = new ReportSection { Title = EXCLUDED_SECTION_TITLE, ExcludeFromWordExport = true };
        report.Add(debug);
        debug.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = EXCLUDED_TEXT });

        return report;
    }
}
