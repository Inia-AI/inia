using IniaAi.Domain.Enums;
using System.Linq.Expressions;

namespace IniaAi.Domain.Entities.Documents;

/// <summary>
/// The one rule for what a page reads as — its text, or its overview when the page is
/// image-only — as an expression so projections translate it and the entity evaluates it.
/// </summary>
public static class PageText
{
    public static readonly Expression<Func<Page, string?>> Readable = page =>
        page.Status == PageStatus.OCRNeedsReview ? page.Overview : page.Text ?? page.RawText;

    private static readonly Func<Page, string?> _readable = Readable.Compile();

    public static string? ReadableOf(Page page)
    {
        return _readable(page);
    }
}
