using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using IniaAi.Common.Content;
using IniaAi.FileFormats.ContractTests.Reports;
using IniaAi.FileFormats.Word;
using IniaAi.FileFormats.Word.OpenXml;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.FileFormats.Tests.Word;

public class OpenXmlWordReportWriterTests
{
    [Fact]
    public async Task WriteAsync_WithNoChartPicture_WritesTheChartsDataAsTheMarkdownExportDoes()
    {
        ChartDataTable expected = ChartDataTable.From(NewsMarketDataFixture.StockPriceChart());
        var writer = new OpenXmlWordReportWriter(NullLogger<OpenXmlWordReportWriter>.Instance);

        BinaryContent file = await writer.WriteAsync(WorkflowDocumentAdapter.ToDocumentElement(NewsMarketDataFixture.ChartOnly()), new ReportFileOptions());

        using var stream = new MemoryStream(file.BinaryData.ToArray());
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, isEditable: false);
        List<List<string>> rows = [.. document.MainDocumentPart!.Document!.Body!.Descendants<Table>().Last().Elements<TableRow>()
            .Select(row => row.Elements<TableCell>().Select(cell => cell.InnerText).ToList())];
        Assert.Equal([[.. expected.Header], .. expected.Rows.Select(row => row.ToList())], rows);
    }
}
