namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>One loaded page.</summary>
internal sealed unsafe class PdfiumPage : IDisposable
{
    public PdfiumPage(PdfiumFile file, int pageNumber)
    {
        int pageCount = PdfiumNative.FPDF_GetPageCount(file.Document);
        if (pageNumber < 1 || pageNumber > pageCount)
        {
            throw new NoSuchPageException($"Page {pageNumber} is not in the PDF, which has {pageCount} pages.");
        }

        Number = pageNumber;
        Handle = PdfiumNative.FPDF_LoadPage(file.Document, pageNumber - 1);
        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"PDFium could not load page {pageNumber}.");
        }

        PdfiumNative.FsRectF bounds;
        _ = PdfiumNative.FPDF_GetPageBoundingBox(Handle, &bounds);
        Box = new PageBox(bounds.Left, bounds.Bottom, bounds.Right, bounds.Top);
        Rotation = PdfiumNative.FPDFPage_GetRotation(Handle);
        DisplayedWidth = PdfiumNative.FPDF_GetPageWidthF(Handle);
        DisplayedHeight = PdfiumNative.FPDF_GetPageHeightF(Handle);
    }

    public int Number { get; }

    public IntPtr Handle { get; }

    /// <summary>The page box in page coordinates, before rotation.</summary>
    public PageBox Box { get; }

    /// <summary>Quarter turns clockwise, 0 to 3.</summary>
    public int Rotation { get; }

    public double DisplayedWidth { get; }

    public double DisplayedHeight { get; }

    /// <summary>
    /// Where an area in page coordinates lands on the page as displayed: rotation applied, origin
    /// at the top left, y growing down. PDFium renders in that space.
    /// </summary>
    public (double Left, double Top, double Right, double Bottom) ToDisplay(PageBox area)
    {
        (double x1, double y1) = ToDisplay(area.Left, area.Bottom);
        (double x2, double y2) = ToDisplay(area.Right, area.Top);
        return (Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    public void Dispose()
    {
        PdfiumNative.FPDF_ClosePage(Handle);
    }

    private (double X, double Y) ToDisplay(double x, double y)
    {
        return Rotation switch
        {
            1 => (y - Box.Bottom, x - Box.Left),
            2 => (Box.Right - x, y - Box.Bottom),
            3 => (Box.Top - y, Box.Right - x),
            _ => (x - Box.Left, Box.Top - y),
        };
    }
}
