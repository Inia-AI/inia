namespace IniaAi.Common.Text;

public static class LinkAddress
{
    private const char SCHEME_END = ':';

    public static readonly IReadOnlySet<string> SafeSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    private static readonly char[] _relativePathStarts = ['/', '?', '#'];

    /// <summary>
    /// Whether a link may keep its address: http, https, mailto or a relative path. A browser drops
    /// whitespace and control characters from an address before it reads the scheme, so they are
    /// dropped here too, and a tab inside "javascript:" does not hide it.
    /// </summary>
    public static bool IsSafe(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return true;
        }

        string address = AsBrowserReadsIt(url);
        return IsRelativeAddress(address) || SafeSchemes.Contains(address[..address.IndexOf(SCHEME_END)]);
    }

    /// <summary>
    /// Whether a link has no scheme of its own, such as "/reports/42" or "#summary", so only a page of the platform
    /// resolves it. An empty address points at the current page, so it is relative too.
    /// </summary>
    public static bool IsRelative(string? url)
    {
        return string.IsNullOrEmpty(url) || IsRelativeAddress(AsBrowserReadsIt(url));
    }

    private static string AsBrowserReadsIt(string url)
    {
        return string.Concat(url.Where(static character => !char.IsWhiteSpace(character) && !char.IsControl(character)));
    }

    private static bool IsRelativeAddress(string address)
    {
        int schemeEnd = address.IndexOf(SCHEME_END);
        int pathStart = address.IndexOfAny(_relativePathStarts);
        return schemeEnd < 0 || (pathStart >= 0 && pathStart < schemeEnd);
    }
}
