namespace IniaAi.Common.Text;

/// <summary>
/// Produces log-safe representations of user-supplied strings. Use this before
/// passing any value that originated from an HTTP request (query string, route,
/// body) into an <c>ILogger</c> template parameter to prevent log-forging
/// attacks, where a caller injects newline characters to fabricate additional
/// log lines.
/// </summary>
public static class LogSanitizer
{
    private const string SCHEME_SEPARATOR = "://";

    private static readonly char[] _queryOrFragment = ['?', '#'];

    /// <summary>
    /// Returns <paramref name="value"/> with CR, LF, and other ASCII control
    /// characters stripped so it is safe to emit as a log-template parameter.
    /// Returns an empty string when <paramref name="value"/> is null so the log
    /// template still produces a readable output.
    /// </summary>
    /// <param name="value">The raw user-supplied value.</param>
    /// <returns>A log-safe copy of <paramref name="value"/>.</returns>
    public static string ForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (char ch in value)
        {
            if (!char.IsControl(ch))
            {
                _ = builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns <paramref name="url"/> as <see cref="ForLog"/> does, with its query, its fragment and the user-info of its
    /// authority left out: a key or a token in an address rides in those parts. The scheme, the host, the port and the
    /// path stay, so an operator can still tell which service was called.
    /// </summary>
    /// <param name="url">The address as the request used it; it need not parse.</param>
    /// <returns>A log-safe copy of <paramref name="url"/> without its secrets.</returns>
    public static string ForLogUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        string address = url.Split(_queryOrFragment, 2)[0];
        int schemeEnd = address.IndexOf(SCHEME_SEPARATOR, StringComparison.Ordinal);
        int authorityStart = schemeEnd < 0 ? 0 : schemeEnd + SCHEME_SEPARATOR.Length;
        int pathStart = address.IndexOf('/', authorityStart);
        int authorityEnd = pathStart < 0 ? address.Length : pathStart;
        int userInfoEnd = address[authorityStart..authorityEnd].LastIndexOf('@');
        if (userInfoEnd >= 0)
        {
            address = address[..authorityStart] + address[(authorityStart + userInfoEnd + 1)..];
        }

        return ForLog(address);
    }
}
