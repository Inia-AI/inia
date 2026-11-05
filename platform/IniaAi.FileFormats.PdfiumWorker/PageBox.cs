namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>A rectangle in PDF points, with the origin at the bottom left.</summary>
internal readonly record struct PageBox(double Left, double Bottom, double Right, double Top)
{
    public bool Contains(double x, double y)
    {
        return x >= Left && x <= Right && y >= Bottom && y <= Top;
    }
}
