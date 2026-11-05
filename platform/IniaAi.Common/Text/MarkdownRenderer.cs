using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.EmphasisExtras;

namespace IniaAi.Common.Text;

public static partial class MarkdownRenderer
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UseAutoLinks()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    [GeneratedRegex(
        @"<img\b(?![^>]*\bsrc\s*=\s*[""']data:image)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NonDataImgRegex();

    [GeneratedRegex(
        @"\balt\s*=\s*[""']([^""']*)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ImgAltRegex();

    [GeneratedRegex(
        @"<a\b(?:[^>""']|""[^""]*""|'[^']*')*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AnchorTagRegex();

    [GeneratedRegex(
        @"\shref\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)'|(?<url>[^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HrefAttributeRegex();

    public static string ToHtml(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        return Markdown.ToHtml(raw, _pipeline);
    }

    public static string ToPlainText(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        return Markdown.ToPlainText(raw, _pipeline).Trim();
    }

    public static string ToHtmlForWordExport(string? raw)
    {
        string html = ToHtml(raw);
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        string withoutLinkedImages = NonDataImgRegex().Replace(html, match =>
        {
            Match altMatch = ImgAltRegex().Match(match.Value);
            return altMatch.Success ? altMatch.Groups[1].Value : string.Empty;
        });
        return WithoutUnsafeLinks(withoutLinkedImages);
    }

    /// <summary>
    /// The HTML for a file the reader downloads, such as a Word report: a link with no address of its own, such as
    /// "/reports/42", keeps its text and loses its address, because the file has no page of the platform to resolve it
    /// against.
    /// </summary>
    public static string WithoutRelativeLinks(string html)
    {
        return WithoutLinks(html, LinkAddress.IsRelative);
    }

    /// <summary>A link whose address could run code or carry a page keeps its text and loses its address, so no export carries it.</summary>
    private static string WithoutUnsafeLinks(string html)
    {
        return WithoutLinks(html, address => !LinkAddress.IsSafe(address));
    }

    private static string WithoutLinks(string html, Func<string, bool> drops)
    {
        return AnchorTagRegex().Replace(html, anchor =>
        {
            Match href = HrefAttributeRegex().Match(anchor.Value);
            return href.Success && drops(WebUtility.HtmlDecode(href.Groups["url"].Value))
                ? anchor.Value.Remove(href.Index, href.Length)
                : anchor.Value;
        });
    }
}
