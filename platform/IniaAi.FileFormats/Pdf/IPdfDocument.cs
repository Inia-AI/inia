using IniaAi.Common.Content;

namespace IniaAi.FileFormats.Pdf;

/// <summary>
/// An open PDF. Page numbers start at 1. An <c>area</c> limits a page to that part of its
/// <see cref="Box"/>, such as one half of a scan of two facing pages.
/// </summary>
public interface IPdfDocument : IDisposable
{
    int PageCount { get; }

    /// <summary>
    /// The file after its startxref pointer was rebuilt on open, for the caller to store; null when
    /// the file opened as it was.
    /// </summary>
    byte[]? RepairedBytes { get; }

    /// <summary>The page in points, as the file lays it out: its rotation is not applied.</summary>
    PdfBox Box(int pageNumber);

    /// <summary>
    /// The page as a viewer shows it: cut to its crop box, rotation applied. A render resolution is
    /// chosen from it.
    /// </summary>
    PdfBox DisplayedBox(int pageNumber);

    string Text(int pageNumber, PdfBox? area = null);

    IReadOnlyList<PdfBox> TextFragmentBoxes(int pageNumber);

    BinaryContent RenderPng(int pageNumber, int dpi, PdfBox? area = null);

    BinaryContent RenderJpeg(int pageNumber, int dpi, int quality, PdfBox? area = null);
}
