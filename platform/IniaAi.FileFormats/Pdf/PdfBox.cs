namespace IniaAi.FileFormats.Pdf;

/// <summary>A rectangle in PDF points, with the origin at the bottom left.</summary>
public readonly record struct PdfBox(double Left, double Bottom, double Right, double Top)
{
    public double Width => Right - Left;

    public double Height => Top - Bottom;

    /// <summary>The left or right half, for a scan of two facing pages.</summary>
    public PdfBox Half(bool isLeftSide)
    {
        double middle = Left + (Width / 2);

        return isLeftSide ? this with { Right = middle } : this with { Left = middle };
    }
}
