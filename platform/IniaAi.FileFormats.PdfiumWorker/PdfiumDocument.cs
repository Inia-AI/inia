using SkiaSharp;
using System.Runtime.InteropServices;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>The answers the worker gives about its open document. Page numbers start at 1.</summary>
internal sealed unsafe class PdfiumDocument(PdfiumFile file) : IDisposable
{
    private const double POINTS_PER_INCH = 72.0;
    private const double PIXEL_ROUNDING = 0.001;
    private const uint WHITE = 0xFFFFFFFF;
    private const int PNG_QUALITY = 100;

    private PdfiumPage? _page;
    private IntPtr _textPage;

    public PdfiumFile File { get; } = file;

    public PageBox Box(int pageNumber)
    {
        return Page(pageNumber).Box;
    }

    public (double Width, double Height) DisplayedSize(int pageNumber)
    {
        PdfiumPage page = Page(pageNumber);
        return (page.DisplayedWidth, page.DisplayedHeight);
    }

    public string Text(int pageNumber, PageBox? area)
    {
        PdfiumPage page = Page(pageNumber);
        return PdfiumPageText.Read(page, TextPage(page), area);
    }

    /// <summary>PDFium's text rectangles, each a run of characters on one line, inside the page box.</summary>
    public List<PageBox> TextFragmentBoxes(int pageNumber)
    {
        PdfiumPage page = Page(pageNumber);
        IntPtr textPage = TextPage(page);
        int count = PdfiumNative.FPDFText_CountRects(textPage, 0, -1);
        var boxes = new List<PageBox>(Math.Max(count, 0));
        for (int index = 0; index < count; index++)
        {
            double left, top, right, bottom;
            if (PdfiumNative.FPDFText_GetRect(textPage, index, &left, &top, &right, &bottom) != 0
                && page.Box.Contains((left + right) / 2, (top + bottom) / 2))
            {
                boxes.Add(new PageBox(left, bottom, right, top));
            }
        }

        return boxes;
    }

    public byte[] Render(int pageNumber, int dpi, PageBox? area, PdfiumWorkerProtocol.ImageFormat format, int quality)
    {
        PdfiumPage page = Page(pageNumber);
        (double left, double top, double right, double bottom) = page.ToDisplay(area ?? page.Box);
        double scale = dpi / POINTS_PER_INCH;
        int width = Math.Max(1, (int)(((right - left) * scale) + PIXEL_ROUNDING));
        int height = Math.Max(1, (int)(((bottom - top) * scale) + PIXEL_ROUNDING));

        IntPtr bitmap = PdfiumNative.FPDFBitmap_Create(width, height, alpha: 0);
        if (bitmap == IntPtr.Zero)
        {
            throw new InvalidOperationException($"PDFium could not allocate a {width}x{height} bitmap for page {pageNumber}.");
        }

        try
        {
            _ = PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, new CULong(WHITE));
            var matrix = new PdfiumNative.FsMatrix
            {
                A = (float)scale,
                D = (float)scale,
                E = (float)(-left * scale),
                F = (float)(-top * scale),
            };
            var clip = new PdfiumNative.FsRectF { Left = 0, Top = 0, Right = width, Bottom = height };
            PdfiumNative.FPDF_RenderPageBitmapWithMatrix(bitmap, page.Handle, &matrix, &clip, PdfiumNative.FPDF_ANNOT);

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using SKImage image = SKImage.FromPixels(info, (IntPtr)PdfiumNative.FPDFBitmap_GetBuffer(bitmap), PdfiumNative.FPDFBitmap_GetStride(bitmap));
            using SKData encoded = format == PdfiumWorkerProtocol.ImageFormat.Png
                ? image.Encode(SKEncodedImageFormat.Png, PNG_QUALITY)
                : image.Encode(SKEncodedImageFormat.Jpeg, quality);
            return encoded.ToArray();
        }
        finally
        {
            PdfiumNative.FPDFBitmap_Destroy(bitmap);
        }
    }

    public void Dispose()
    {
        ClosePage();
        File.Dispose();
    }

    /// <summary>The page, kept loaded for the next request: the server asks several things about one page in a row.</summary>
    private PdfiumPage Page(int pageNumber)
    {
        if (_page?.Number != pageNumber)
        {
            ClosePage();
            _page = new PdfiumPage(File, pageNumber);
        }

        return _page;
    }

    private IntPtr TextPage(PdfiumPage page)
    {
        if (_textPage == IntPtr.Zero)
        {
            _textPage = PdfiumNative.FPDFText_LoadPage(page.Handle);
        }

        return _textPage;
    }

    private void ClosePage()
    {
        if (_textPage != IntPtr.Zero)
        {
            PdfiumNative.FPDFText_ClosePage(_textPage);
            _textPage = IntPtr.Zero;
        }

        _page?.Dispose();
        _page = null;
    }
}
