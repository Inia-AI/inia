using IniaAi.Common.Content;

namespace IniaAi.FileFormats.Word;

/// <summary>
/// Reads a Word document page by page. A page is the reader's own split of the text: a reader with a layout engine may
/// take its layout pages, and one without may count words. Word renderers break pages differently anyway, so a caller
/// relies only on every word in order, on a long document spanning several pages, on a picture sitting on the page of
/// the text before it, and on the same bytes splitting the same way on every call with the same fonts installed,
/// because a resumed run matches its stored pages by number. It never relies on a page number matching Word's. A document that still needs its password
/// fails with <see cref="Unlock.DocumentPasswordProtectedException"/> naming <c>documentName</c>.
/// </summary>
public interface IWordPageReader
{
    /// <summary>
    /// The text and image regions of each page, in order. An image region that must be rendered is
    /// rendered to fit <paramref name="visionTier"/>, the largest image the model that reads it reads as it is.
    /// </summary>
    IReadOnlyList<WordPageContent> ExtractPages(byte[] documentBytes, string documentName, VisionTier visionTier);

    /// <summary>
    /// The text of each page, in order, up to <paramref name="maxPages"/> leading pages: a caller that
    /// reads only the first pages skips extracting the rest, and no image region is prepared.
    /// </summary>
    IReadOnlyList<string> ExtractPageTexts(byte[] documentBytes, string documentName, int maxPages);
}
