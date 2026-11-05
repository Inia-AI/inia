using Microsoft.Extensions.Logging;
using System.Text;

namespace IniaAi.FileFormats.Pdf;

public class PdfExtractor
{
    private readonly ILogger _logger;

    public PdfExtractor(ILogger<PdfExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// The text layer page by page, for a reader that can work with part of a file: a page that
    /// fails is skipped with a warning, and reading stops once the text holds
    /// <paramref name="characterBudget"/> characters. The anonymiser reads a file whole and fails on
    /// a bad page instead, because it must not pass a page it could not read.
    /// </summary>
    public static string ExtractTextSkippingFailedPages(IPdfDocument pdf, int characterBudget, ILogger logger)
    {
        var text = new StringBuilder();

        for (int pageNumber = 1; pageNumber <= pdf.PageCount && text.Length < characterBudget; pageNumber++)
        {
            try
            {
                string pageText = pdf.Text(pageNumber);
                _ = text.Append("\n\n").Append(pageText);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Text extraction skips page {PageNumber}: it does not parse.", pageNumber);
            }
        }

        return text.ToString().Trim();
    }

    /// <summary>
    /// Extract text from specific pages of a PDF file, by page number.
    /// <para>
    /// <strong>NOTE: <paramref name="pagesNumbers"/> is 1-based.</strong>
    /// </para>
    /// </summary>
    /// <param name="pdf">The PDF file as a byte array.</param>
    /// <param name="pagesNumbers">The page numbers to extract text from.</param>
    /// <returns>A list of <see cref="PdfExtractionPage"/> containing the extracted
    /// text and metadata.</returns>
    public List<PdfExtractionPage> ExtractPaginatedText(
        IPdfDocument pdf,
        IEnumerable<int> pagesNumbers)
    {
        _logger.LogDebug(
            "Starting text extraction for pages: {pages}",
            string.Join(", ", pagesNumbers));

        int page_count = pdf.PageCount;
        pagesNumbers = pagesNumbers
            .Where(p => p > 0 && p <= page_count)
            .OrderBy(p => p);
        List<PdfExtractionPage> pages = [];

        foreach (int documentPageNumber in pagesNumbers)
        {
            try
            {
                bool isSinglePage = CheckIsSinglePage(
                    pdf,
                    documentPageNumber,
                    out string pageText);

                if (isSinglePage)
                {
                    var page = new PdfExtractionPage(
                        FilePageNumber: documentPageNumber,
                        IsLeftSide: null,
                        Text: pageText);

                    pages.Add(page);

                    continue;
                }

                var leftPage = new PdfExtractionPage(
                    FilePageNumber: documentPageNumber,
                    IsLeftSide: true,
                    Text: pdf.Text(documentPageNumber, pdf.Box(documentPageNumber).Half(isLeftSide: true)));
                pages.Add(leftPage);

                var rightPage = new PdfExtractionPage(
                    FilePageNumber: documentPageNumber,
                    IsLeftSide: false,
                    Text: pdf.Text(documentPageNumber, pdf.Box(documentPageNumber).Half(isLeftSide: false)));
                pages.Add(rightPage);
            }
            catch
            {
                _logger.LogDebug(
                    "Error extracting text from page {pageNumber}",
                    documentPageNumber);
            }
        }

        return pages;
    }

    /// <summary>
    /// The text fragments in the gap between the halves of a page, the middle tenth of its width, and the ones wholly in
    /// its left or right half: the counts that decide whether a page is a two-page spread.
    /// </summary>
    public static (int InGap, int Left, int Right) CountFragmentSides(IReadOnlyList<PdfBox> fragments, double pageWidth)
    {
        double gapStart = pageWidth * 0.45;
        double gapEnd = pageWidth * 0.55;
        int inGap = 0;
        int left = 0;
        int right = 0;
        foreach (PdfBox fragment in fragments)
        {
            if (fragment.Left >= gapStart && fragment.Right <= gapEnd)
            {
                inGap++;
            }
            else if (fragment.Right < pageWidth / 2)
            {
                left++;
            }
            else if (fragment.Left > pageWidth / 2)
            {
                right++;
            }
        }

        return (inGap, left, right);
    }

    private bool CheckIsSinglePage(
        IPdfDocument pdf,
        int documentPageNumber,
        out string pageText)
    {
        _logger.LogDebug(
            "Checking if page {pageNumber} is single or double page",
            documentPageNumber);

        PdfBox pageBox = pdf.Box(documentPageNumber);
        pageText = pdf.Text(documentPageNumber);

        double aspectRatio = pageBox.Width / pageBox.Height;

        if (aspectRatio < 1.4)
        {
            _logger.LogDebug(
                "Page {pageNumber} aspect ratio {aspectRatio} indicates single page",
                documentPageNumber,
                aspectRatio);

            return true;
        }

        if (pageText.Length < 50)
        {
            _logger.LogDebug(
                "Page {pageNumber} has too little text ({textLength} chars) to be double page",
                documentPageNumber,
                pageText.Length);

            return true;
        }

        IReadOnlyList<PdfBox> fragments = pdf.TextFragmentBoxes(documentPageNumber);

        if (fragments.Count < 10)
        {
            _logger.LogDebug(
                "Page {pageNumber} has too few text fragments ({fragmentCount}) to be double page",
                documentPageNumber,
                fragments.Count);

            return true;
        }

        int totalFragments = fragments.Count;
        (int fragmentsInGap, int leftFragments, int rightFragments) = CountFragmentSides(fragments, pageBox.Width);

        double gapDensity = (double)fragmentsInGap / totalFragments;

        if (gapDensity >= 0.05)
        {
            _logger.LogDebug(
                "Page {pageNumber} has high gap density ({gapDensity:P2}) indicating single page",
                documentPageNumber,
                gapDensity);

            return true;
        }

        if (leftFragments == 0 || rightFragments == 0)
        {
            _logger.LogDebug(
                "Page {pageNumber} has all fragments on one side (L:{leftFragments}, R:{rightFragments}) indicating single page",
                documentPageNumber,
                leftFragments,
                rightFragments);

            return true;
        }

        double balanceRatio = (double)leftFragments / rightFragments;

        if (balanceRatio is < 0.3 or > 3.0)
        {
            _logger.LogDebug(
                "Page {pageNumber} has unbalanced fragments (L:{leftFragments}, R:{rightFragments}, Ratio:{balanceRatio:F2}) indicating single page",
                documentPageNumber,
                leftFragments,
                rightFragments,
                balanceRatio);

            return true;
        }

        _logger.LogDebug(
            "Page {pageNumber} determined to be double page",
            documentPageNumber);

        return false;
    }
}

public record PdfExtractionPage(int FilePageNumber, bool? IsLeftSide, string Text);
