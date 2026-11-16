namespace IniaAi.Common.Http;

/// <summary>
/// An outbound request, or a redirect it followed, aimed at a web address outside the set its caller
/// may reach. It is not an <see cref="HttpRequestException"/> on purpose: a caller that retries
/// network errors must not retry a refusal.
/// </summary>
public sealed class OutboundOriginRefusedException : Exception
{
    public OutboundOriginRefusedException(string origin)
        : base($"Refused to send a request to {origin}: it is not one of the web addresses approved for this report. Ask a super admin to approve it for the workflow type.")
    {
        Origin = origin;
    }

    /// <summary>The refused scheme, host and port, such as <c>https://api.example.com</c>.</summary>
    public string Origin { get; }
}
