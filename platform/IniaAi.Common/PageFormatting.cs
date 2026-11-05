using System.Text.RegularExpressions;

namespace IniaAi.Common;

/// <summary>
/// Common utilities for page formatting patterns used across RAG and filtering operations
/// </summary>
public static partial class PageFormatting
{
    /// <summary>
    /// Formats a page tag with the given page number, document name, and content
    /// </summary>
    /// <param name="pageNumber">The page number (1-based)</param>
    /// <param name="documentName">The document name (should have spaces replaced with dashes)</param>
    /// <param name="content">The page content</param>
    /// <returns>Formatted page tag</returns>
    public static string FormatPageTag(int pageNumber, string documentName, string? content)
    {
        return $"<{FormatPageName(pageNumber, documentName)}>\n{content}\n</{FormatPageName(pageNumber, documentName)}>";
    }

    public static string FormatPageName(int pageNumber, string documentName)
    {
        return $"page{pageNumber}({documentName})";
    }

    /// <summary>
    /// Gets the regex pattern that matches the format produced by FormatPageTag.
    /// This ensures the regex stays in sync with the formatting method.
    /// </summary>
    /// <returns>The regex pattern string</returns>
    public static string GetPageTagPattern()
    {
        // This pattern is derived from the FormatPageTag method structure:
        // <pageN(documentName)>\ncontent\n</pageN(documentName)>
        return @"<page(\d+)\(([^)]+)\)>\s*(.*?)\s*</page\1\(\2\)>";
    }

    /// <summary>
    /// Sanitizes a document name by replacing spaces with dashes
    /// </summary>
    /// <param name="documentName">The original document name</param>
    /// <returns>Sanitized document name with spaces replaced by dashes</returns>
    public static string SanitizeDocumentName(string documentName)
    {
        return documentName.Replace(" ", "-");
    }

    /// <summary>
    /// Creates a regex for matching page tags using the pattern that matches FormatPageTag output
    /// </summary>
    /// <returns>Compiled regex for page tag matching</returns>
    public static Regex CreatePageTagRegex()
    {
        return new Regex(GetPageTagPattern(), RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    /// <summary>
    /// A page's text as a model receives it: wrapped in the page's text markers, with a
    /// "Source URL" line first when the page came from the web. <see cref="RemovePageMarkers"/>
    /// strips exactly this wrapping, so the two change together.
    /// </summary>
    public static string FormatPageText(string pageName, string text, string? sourceUrl)
    {
        string sourceUrlLine = string.IsNullOrWhiteSpace(sourceUrl)
            ? string.Empty
            : $"Source URL: {sourceUrl}\n\n";

        return $"<{pageName}-text>\n{sourceUrlLine}{text}\n</{pageName}-text>";
    }

    /// <summary>
    /// Removes the markers <see cref="FormatPageText"/> wraps around a page's text, and its
    /// "Source URL" line, from text a model quoted back. A citation quote that starts or ends at a
    /// page boundary carries the marker, and a reader must not see it. Text without a marker comes
    /// back unchanged.
    /// </summary>
    public static string RemovePageMarkers(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string cleaned = PageMarkerRegex().Replace(text, string.Empty);
        return cleaned.Length == text.Length ? text : cleaned.Trim();
    }

    [GeneratedRegex(@"</?page\d+\(.*?\)-text>[ \t]*(?:\r?\n)?(?:Source URL: [^\r\n]*\r?\n)?", RegexOptions.IgnoreCase)]
    private static partial Regex PageMarkerRegex();
}
