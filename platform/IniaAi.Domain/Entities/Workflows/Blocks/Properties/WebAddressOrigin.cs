using IniaAi.Common.Http;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// The part of a Web API call address that its author writes and no placeholder value can change:
/// the scheme, the host and the port.
/// </summary>
public static class WebAddressOrigin
{
    private static readonly char[] _hostEnds = ['/', '?', '#'];
    private static readonly char[] _placeholderMarks = ['{', '}'];

    /// <summary>
    /// The scheme, host and port that <paramref name="template"/> names, such as
    /// <c>https://api.example.com</c>, or null when a placeholder could choose them: the address does
    /// not start with http or https, or a placeholder comes before the first "/", "?" or "#" after
    /// the host.
    /// </summary>
    public static string? Of(string template)
    {
        int separator = template.IndexOf(Uri.SchemeDelimiter, StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        int hostEnd = template.IndexOfAny(_hostEnds, separator + Uri.SchemeDelimiter.Length);
        string head = hostEnd < 0 ? template : template[..hostEnd];
        if (head.IndexOfAny(_placeholderMarks) >= 0
            || !Uri.TryCreate(head, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.Host.Length == 0)
        {
            return null;
        }

        return OutboundOrigin.Of(uri);
    }
}
