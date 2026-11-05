using IniaAi.Common.Content;
using IniaAi.Common.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;

namespace IniaAi.FileFormats.Word;

/// <summary>
/// Writes a run's report as a file in one export format. A build exports the formats it registers a writer for, and a
/// writer registered later replaces an earlier one of the same format.
/// </summary>
public interface IReportFileWriter
{
    ExportFormat Format { get; }

    /// <summary>The file name extension, with its dot.</summary>
    string Extension { get; }

    string ContentType { get; }

    Task<BinaryContent> WriteAsync(IDocumentElement document, ReportFileOptions options);
}

/// <param name="ChartRenderService">Draws each chart as an image; null puts each chart in as a table of its data.</param>
/// <param name="ReportLayout">The page size and orientation of a paged format.</param>
/// <param name="ReportContext">The report's name, which groups the chart debug files of one report.</param>
/// <param name="FooterNotice">One line of plain text the file carries at its foot, on every page of a paged format; null for none.</param>
public sealed record ReportFileOptions(
    IChartImageRenderService? ChartRenderService = null,
    ReportLayout? ReportLayout = null,
    string? ReportContext = null,
    string? FooterNotice = null);

public static class ReportFileFormats
{
    /// <summary>The writer of each format a build exports: for a format with several writers, the one registered last.</summary>
    public static IReadOnlyDictionary<ExportFormat, IReportFileWriter> ByFormat(IEnumerable<IReportFileWriter> writers)
    {
        return writers.GroupBy(writer => writer.Format).ToDictionary(group => group.Key, group => group.Last());
    }

    /// <summary>The refusal for a format no registered writer exports.</summary>
    public static string Unavailable(ExportFormat format)
    {
        return $"There is no {format} export.";
    }
}
