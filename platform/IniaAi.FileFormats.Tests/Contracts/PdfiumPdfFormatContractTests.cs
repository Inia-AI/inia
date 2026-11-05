using IniaAi.FileFormats.ContractTests.Pdf;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Pdf.Pdfium;

namespace IniaAi.FileFormats.Tests.Contracts;

public class PdfiumPdfFormatContractTests : PdfFormatContract
{
    protected override IPdfFormat Format { get; } = new PdfiumPdfFormat();
}
