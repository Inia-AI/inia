using IniaAi.Common.Text;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;

namespace IniaAi.FileFormats.Word;

/// <summary>Turns a run's report into the element tree that every <see cref="IReportFileWriter"/> writes.</summary>
public static class WorkflowDocumentAdapter
{
    public static DocumentElement ToDocumentElement(WorkflowReport report, string? analystDisplayName = null)
    {
        var doc = new DocumentElement
        {
            Title = report.Title?.Trim(),
            Type = DocumentElementType.Paragraph,
            Order = 0
        };

        doc.Metadata[DocumentMetadataKeys.COUNTERPARTY] = report.Counterparty?.Name;
        doc.Metadata[DocumentMetadataKeys.ANALYST_NAME] = !string.IsNullOrWhiteSpace(analystDisplayName)
            ? analystDisplayName
            : report.AnalystName;

        foreach (ReportSection section in report.Sections.OrderBy(s => s.Order))
        {
            DocumentElement? sectionElement = ToDocumentElement(section);
            if (sectionElement is not null)
            {
                doc.Children.Add(sectionElement);
            }
        }

        return doc;
    }

    private static DocumentElement? ToDocumentElement(ReportSection section)
    {
        if (section.ExcludeFromWordExport || section.Paragraphs.Count == 0)
        {
            return null;
        }

        var doc = new DocumentElement
        {
            Title = section.Title?.Trim(),
            Type = DocumentElementType.Section,
            Order = section.Order
        };

        if (section.ShownLayout() is { } layout)
        {
            doc.OutputData.Add(layout);
        }

        doc.Metadata["Order"] = section.Order.ToString();

        foreach (ReportParagraph paragraph in section.Paragraphs.OrderBy(p => p.Order))
        {
            doc.Children.Add(ToDocumentElement(paragraph));
        }

        return doc;
    }

    private static DocumentElement ToDocumentElement(ReportParagraph paragraph)
    {
        var doc = new DocumentElement
        {
            Type = MapKind(paragraph.Kind),
            Title = paragraph.Title?.Trim(),
            Subtitle = paragraph.Subtitle?.Trim(),
            Content = MarkdownRenderer.ToHtmlForWordExport(FactTags.Strip(paragraph.Content)?.Trim()),
            Order = paragraph.Order
        };

        if (paragraph.Kind == ReportParagraphKind.DynamicChart && paragraph.ChartPayload is not null)
        {
            doc.OutputData.Add(paragraph.ChartPayload);
        }

        doc.Metadata["Order"] = paragraph.Order.ToString();

        return doc;
    }

    private static DocumentElementType MapKind(ReportParagraphKind kind)
    {
        return kind switch
        {
            ReportParagraphKind.Paragraph => DocumentElementType.Paragraph,
            ReportParagraphKind.DynamicChart => DocumentElementType.Chart,
            ReportParagraphKind.Divider => DocumentElementType.Divider,
            ReportParagraphKind.Table => DocumentElementType.Table,
            _ => DocumentElementType.Paragraph
        };
    }
}
