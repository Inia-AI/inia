using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Pdf.Pdfium;
using IniaAi.Testing.Documents;

namespace IniaAi.FileFormats.Tests.Pdf;

public class PdfiumPdfFormatTests
{
    [Fact]
    public void Text_LeavesOutTextOutsideTheCropBox_AsAViewerShowsThePage()
    {
        byte[] file = PdfFixtures.WithTextOutsideTheCropBox("Shown line", "Cropped line");

        using IPdfDocument pdf = new PdfiumPdfFormat().Open(new MemoryStream(file), "cropped.pdf");

        string text = pdf.Text(1);
        Assert.Contains("Shown line", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Cropped line", text, StringComparison.Ordinal);
        Assert.Single(pdf.TextFragmentBoxes(1));
    }
}
