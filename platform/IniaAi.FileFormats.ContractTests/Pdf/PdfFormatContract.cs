using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.FileFormats.Images;
using IniaAi.FileFormats.Pdf;
using IniaAi.FileFormats.Unlock;
using Microsoft.Extensions.Logging.Abstractions;
using static IniaAi.FileFormats.ContractTests.ContractFixture;

namespace IniaAi.FileFormats.ContractTests.Pdf;

/// <summary>
/// What the document pipeline relies on from every <see cref="IPdfFormat"/>: the doc comments of
/// <see cref="IPdfFormat"/> and <see cref="IPdfDocument"/>, the page split of <see cref="PdfExtractor"/>,
/// the OCR routing of blank pages, and page renders sized from <see cref="IPdfDocument.DisplayedBox"/>.
/// </summary>
public abstract class PdfFormatContract
{
    private const string FIRST_PAGE_TEXT = "Alpha page: the first of three pages.";
    private const string SECOND_PAGE_TEXT = "Bravo page: the second of three pages.";
    private const string THIRD_PAGE_TEXT = "Charlie page: the third of three pages.";
    private const string LEFT_COLUMN = "Left column line";
    private const string RIGHT_COLUMN = "Right column line";
    private const string FIRST_LEFT_LINE = "Left column line 01 of the spread";
    private const string FIRST_RIGHT_LINE = "Right column line 01 of the spread";

    private const double LETTER_WIDTH_PT = 612;
    private const double LETTER_HEIGHT_PT = 792;
    private const double CROPPED_WIDTH_PT = 540;
    private const double CROPPED_HEIGHT_PT = 720;
    private const double SPREAD_WIDTH_PT = 1224;
    private const int ROTATED_PAGE = 1;
    private const int CROPPED_PAGE = 2;
    private const int ONE_PIXEL_PER_POINT_DPI = 72;
    private const int JPEG_QUALITY = 80;
    private const int PIXEL_TOLERANCE = 1;

    private static readonly PictureSize _halfSpreadAtOnePixelPerPoint = new((int)(SPREAD_WIDTH_PT / 2), (int)LETTER_HEIGHT_PT);

    protected abstract IPdfFormat Format { get; }

    [Fact]
    public void Open_CountsThePages_AndHandsBackNoRepairForAFileThatOpensAsItIs()
    {
        using IPdfDocument pdf = Open(THREE_PAGES_PDF);

        Assert.Equal(3, pdf.PageCount);
        Assert.Null(pdf.RepairedBytes);
    }

    [Fact]
    public void Text_ReadsThePageItIsAskedFor_CountingFromOne()
    {
        using IPdfDocument pdf = Open(THREE_PAGES_PDF);

        Assert.Contains(FIRST_PAGE_TEXT, ExtractedText.Words(pdf.Text(1)));
        Assert.Contains(SECOND_PAGE_TEXT, ExtractedText.Words(pdf.Text(2)));
        Assert.DoesNotContain(FIRST_PAGE_TEXT, ExtractedText.Words(pdf.Text(2)));
        Assert.Contains(THIRD_PAGE_TEXT, ExtractedText.Words(pdf.Text(3)));
    }

    [Fact]
    public void Text_OfAPageWithNoTextLayer_IsBlank_SoThePipelineSendsItToOcr()
    {
        using IPdfDocument pdf = Open(IMAGE_ONLY_PDF);

        string text = pdf.Text(1);

        Assert.Equal(1, pdf.PageCount);
        Assert.True(string.IsNullOrWhiteSpace(text), $"A scan with no text layer read as \"{text}\".");
    }

    [Fact]
    public void Box_IsThePageInPoints_WithoutItsRotation()
    {
        using IPdfDocument pdf = Open(ROTATED_AND_CROPPED_PDF);

        PdfBox box = pdf.Box(ROTATED_PAGE);

        Assert.Equal(LETTER_WIDTH_PT, box.Width);
        Assert.Equal(LETTER_HEIGHT_PT, box.Height);
    }

    [Fact]
    public void DisplayedBox_IsThePageAsAViewerShowsIt_TurnedByItsRotation_AndCutToItsCropBox()
    {
        using IPdfDocument pdf = Open(ROTATED_AND_CROPPED_PDF);

        PdfBox turned = pdf.DisplayedBox(ROTATED_PAGE);
        PdfBox cropped = pdf.DisplayedBox(CROPPED_PAGE);

        Assert.Equal((LETTER_HEIGHT_PT, LETTER_WIDTH_PT), (turned.Width, turned.Height));
        Assert.Equal((CROPPED_WIDTH_PT, CROPPED_HEIGHT_PT), (cropped.Width, cropped.Height));
    }

    [Theory]
    [InlineData(ROTATED_PAGE, ONE_PIXEL_PER_POINT_DPI)]
    [InlineData(ROTATED_PAGE, ONE_PIXEL_PER_POINT_DPI * 2)]
    [InlineData(CROPPED_PAGE, ONE_PIXEL_PER_POINT_DPI)]
    [InlineData(CROPPED_PAGE, ONE_PIXEL_PER_POINT_DPI * 2)]
    public void RenderPng_IsTheDisplayedBoxAtTheAskedResolution_SoARenderSizedFromItFitsTheModel(int pageNumber, int dpi)
    {
        using IPdfDocument pdf = Open(ROTATED_AND_CROPPED_PDF);

        BinaryContent page = pdf.RenderPng(pageNumber, dpi);

        Assert.Equal(MediaType.ImagePng, page.MediaType);
        AssertSize(PixelsOf(pdf.DisplayedBox(pageNumber), dpi), page);
    }

    [Fact]
    public void RenderPng_OfTheLargestPageAPdfMayDeclare_AtTheDpiThePipelineResolves_FitsTheStandardTier()
    {
        const double PDF_MAXIMUM_PAGE_POINTS = 14_400;
        using IPdfDocument pdf = Format.Open(new MemoryStream(OnePagePdf(PDF_MAXIMUM_PAGE_POINTS, PDF_MAXIMUM_PAGE_POINTS)), "poster.pdf");
        int dpi = ModelImageLimits.ResolveRenderDpi(PDF_MAXIMUM_PAGE_POINTS, PDF_MAXIMUM_PAGE_POINTS, VisionTier.Standard);

        PictureSize rendered = Pictures.ReadSize(pdf.RenderPng(1, dpi).BinaryData.ToArray())!;

        Assert.InRange(rendered.Width, 1, VisionTier.Standard.LongEdgePx);
        Assert.InRange(rendered.Height, 1, VisionTier.Standard.LongEdgePx);
    }

    /// <summary>A PDF of one empty page of <paramref name="widthPt"/> by <paramref name="heightPt"/> points, with a correct cross-reference table.</summary>
    private static byte[] OnePagePdf(double widthPt, double heightPt)
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPt} {heightPt}] /Contents 4 0 R /Resources << >> >>"),
            "<< /Length 0 >>\nstream\n\nendstream",
        ];
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            _ = pdf.Append(FormattableString.Invariant($"{index + 1} 0 obj\n{objects[index]}\nendobj\n"));
        }

        int crossReference = pdf.Length;
        _ = pdf.Append(FormattableString.Invariant($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n"));
        foreach (int offset in offsets)
        {
            _ = pdf.Append(FormattableString.Invariant($"{offset:D10} 00000 n \n"));
        }

        _ = pdf.Append(FormattableString.Invariant($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{crossReference}\n%%EOF\n"));
        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }

    [Fact]
    public void RenderJpeg_IsAJpegOfThePageAtTheAskedResolution()
    {
        using IPdfDocument pdf = Open(THREE_PAGES_PDF);

        BinaryContent page = pdf.RenderJpeg(2, ONE_PIXEL_PER_POINT_DPI * 2, JPEG_QUALITY);

        Assert.Equal(MediaType.ImageJpeg, page.MediaType);
        AssertSize(PixelsOf(pdf.DisplayedBox(2), ONE_PIXEL_PER_POINT_DPI * 2), page);
    }

    [Fact]
    public void RenderPng_OfEachHalfOfFacingPages_DrawsThatHalf_AndLeavesThePageAsItWas()
    {
        using IPdfDocument pdf = Open(FACING_PAGES_PDF);
        PdfBox page = pdf.Box(1);

        BinaryContent left = pdf.RenderPng(1, ONE_PIXEL_PER_POINT_DPI, page.Half(isLeftSide: true));
        BinaryContent right = pdf.RenderPng(1, ONE_PIXEL_PER_POINT_DPI, page.Half(isLeftSide: false));

        AssertSize(_halfSpreadAtOnePixelPerPoint, left);
        AssertSize(_halfSpreadAtOnePixelPerPoint, right);
        Assert.Equal(page, pdf.Box(1));
        Assert.Contains(FIRST_RIGHT_LINE, ExtractedText.Words(pdf.Text(1)));
    }

    [Fact]
    public void RenderJpeg_OfAnArea_DrawsThatArea()
    {
        using IPdfDocument pdf = Open(FACING_PAGES_PDF);

        BinaryContent left = pdf.RenderJpeg(1, ONE_PIXEL_PER_POINT_DPI, JPEG_QUALITY, pdf.Box(1).Half(isLeftSide: true));

        Assert.Equal(MediaType.ImageJpeg, left.MediaType);
        AssertSize(_halfSpreadAtOnePixelPerPoint, left);
    }

    [Fact]
    public void Text_OfAnArea_ReadsOnlyTheTextInsideIt()
    {
        using IPdfDocument pdf = Open(FACING_PAGES_PDF);

        string left = ExtractedText.Words(pdf.Text(1, pdf.Box(1).Half(isLeftSide: true)));
        string right = ExtractedText.Words(pdf.Text(1, pdf.Box(1).Half(isLeftSide: false)));

        Assert.Contains(FIRST_LEFT_LINE, left);
        Assert.DoesNotContain(RIGHT_COLUMN, left);
        Assert.Contains(FIRST_RIGHT_LINE, right);
        Assert.DoesNotContain(LEFT_COLUMN, right);
    }

    [Fact]
    public void TextFragmentBoxes_LieOnThePage_OverTheText()
    {
        using IPdfDocument pdf = Open(FACING_PAGES_PDF);
        PdfBox page = pdf.Box(1);

        IReadOnlyList<PdfBox> fragments = pdf.TextFragmentBoxes(1);

        Assert.NotEmpty(fragments);
        Assert.All(fragments, fragment =>
        {
            Assert.InRange(fragment.Left, page.Left, fragment.Right);
            Assert.InRange(fragment.Right, fragment.Left, page.Right);
            Assert.InRange(fragment.Bottom, page.Bottom, fragment.Top);
            Assert.InRange(fragment.Top, fragment.Bottom, page.Top);
        });
    }

    [Fact]
    public void TextFragmentBoxes_OfFacingPages_LetTheExtractorSplitThemIntoTwoPages()
    {
        using IPdfDocument pdf = Open(FACING_PAGES_PDF);

        List<PdfExtractionPage> pages = new PdfExtractor(NullLogger<PdfExtractor>.Instance).ExtractPaginatedText(pdf, [1]);

        Assert.Collection(
            pages,
            left =>
            {
                Assert.True(left.IsLeftSide);
                Assert.Contains(FIRST_LEFT_LINE, ExtractedText.Words(left.Text));
                Assert.DoesNotContain(RIGHT_COLUMN, left.Text);
            },
            right =>
            {
                Assert.False(right.IsLeftSide);
                Assert.Contains(FIRST_RIGHT_LINE, ExtractedText.Words(right.Text));
                Assert.DoesNotContain(LEFT_COLUMN, right.Text);
            });
    }

    [Fact]
    public void TextFragmentBoxes_OfSinglePages_LetTheExtractorKeepEachWhole()
    {
        using IPdfDocument pdf = Open(THREE_PAGES_PDF);

        List<PdfExtractionPage> pages = new PdfExtractor(NullLogger<PdfExtractor>.Instance).ExtractPaginatedText(pdf, [1, 2, 3]);

        Assert.Equal([1, 2, 3], pages.Select(page => page.FilePageNumber));
        Assert.All(pages, page => Assert.Null(page.IsLeftSide));
        Assert.Contains(SECOND_PAGE_TEXT, ExtractedText.Words(pages[1].Text));
    }

    [Theory]
    [InlineData(PROTECTED_PDF)]
    [InlineData(PROTECTED_PDF_AES256)]
    public void Open_AFileThatStillNeedsItsPassword_FailsNamingTheDocument(string fixture)
    {
        IPdfFormat format = Format;
        string name = ContractFixture.NameOf(fixture);

        DocumentPasswordProtectedException failure = Assert.Throws<DocumentPasswordProtectedException>(
            () => format.Open(ContractFixture.Stream(fixture), name));

        Assert.Equal(name, failure.DocumentName);
    }

    [Fact]
    public void Open_AFileWithOnlyAnOwnerPassword_ReadsItWithoutOne()
    {
        using IPdfDocument pdf = Open(OWNER_RESTRICTED_PDF);

        Assert.Equal(3, pdf.PageCount);
        Assert.Contains(FIRST_PAGE_TEXT, ExtractedText.Words(pdf.Text(1)));
    }

    [Fact]
    public void Open_AFileCutOffInItsCrossReference_ReadsEveryPage_AndAnyRepairItHandsBackOpensAsItIs()
    {
        using IPdfDocument pdf = Open(TRUNCATED_PDF);

        Assert.Equal(3, pdf.PageCount);
        Assert.Contains(THIRD_PAGE_TEXT, ExtractedText.Words(pdf.Text(3)));
        AssertRepairOpensAsItIs(pdf);
    }

    [Fact]
    public void Open_WithARewriteBeforeRendering_ReadsAndRendersEveryPage()
    {
        using IPdfDocument pdf = Open(TRUNCATED_PDF, rewriteBeforeRendering: true);

        Assert.Equal(3, pdf.PageCount);
        Assert.Contains(SECOND_PAGE_TEXT, ExtractedText.Words(pdf.Text(2)));
        AssertSize(new PictureSize((int)LETTER_WIDTH_PT, (int)LETTER_HEIGHT_PT), pdf.RenderPng(2, ONE_PIXEL_PER_POINT_DPI));
        AssertRepairOpensAsItIs(pdf);
    }

    [Fact]
    public void Dispose_DisposesTheStreamThatOpenTookOver()
    {
        MemoryStream stream = ContractFixture.Stream(THREE_PAGES_PDF);

        Format.Open(stream, ContractFixture.NameOf(THREE_PAGES_PDF)).Dispose();

        Assert.False(stream.CanRead);
    }

    [Fact]
    public void Open_ThatFails_DisposesTheStream()
    {
        MemoryStream stream = ContractFixture.Stream(PROTECTED_PDF);

        _ = Assert.Throws<DocumentPasswordProtectedException>(() => Format.Open(stream, ContractFixture.NameOf(PROTECTED_PDF)));

        Assert.False(stream.CanRead);
    }

    private IPdfDocument Open(string fixture, bool rewriteBeforeRendering = false)
    {
        return Format.Open(ContractFixture.Stream(fixture), ContractFixture.NameOf(fixture), rewriteBeforeRendering);
    }

    private void AssertRepairOpensAsItIs(IPdfDocument pdf)
    {
        if (pdf.RepairedBytes is not byte[] repaired)
        {
            return;
        }

        Assert.True(PdfUtils.HasValidStructure(repaired));
        using IPdfDocument reopened = Format.Open(new MemoryStream(repaired), ContractFixture.NameOf(TRUNCATED_PDF));
        Assert.Null(reopened.RepairedBytes);
        Assert.Equal(pdf.PageCount, reopened.PageCount);
    }

    private static PictureSize PixelsOf(PdfBox box, int dpi)
    {
        return new PictureSize(
            (int)Math.Round(box.Width * dpi / PdfUtils.POINTS_PER_INCH),
            (int)Math.Round(box.Height * dpi / PdfUtils.POINTS_PER_INCH));
    }

    private static void AssertSize(PictureSize expected, BinaryContent image)
    {
        PictureSize? actual = Pictures.ReadSize(image.BinaryData.ToArray());

        Assert.NotNull(actual);
        Assert.True(
            Math.Abs(actual.Width - expected.Width) <= PIXEL_TOLERANCE && Math.Abs(actual.Height - expected.Height) <= PIXEL_TOLERANCE,
            $"Expected {expected.Width} x {expected.Height} px, got {actual.Width} x {actual.Height} px.");
    }
}
