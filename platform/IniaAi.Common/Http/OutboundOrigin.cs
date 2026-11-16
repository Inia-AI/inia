namespace IniaAi.Common.Http;

/// <summary>The scheme, host and port of an address, such as <c>https://api.example.com</c>, as an allowed set of web addresses holds them.</summary>
public static class OutboundOrigin
{
    public static string Of(Uri address)
    {
        return $"{address.Scheme}{Uri.SchemeDelimiter}{address.Authority}";
    }

    public static bool Same(Uri first, Uri second)
    {
        return string.Equals(Of(first), Of(second), StringComparison.OrdinalIgnoreCase);
    }
}
