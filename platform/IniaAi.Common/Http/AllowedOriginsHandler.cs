namespace IniaAi.Common.Http;

/// <summary>
/// Names the web addresses one caller's requests may reach on every request it passes on, so
/// <see cref="CheckedRedirectHandler"/> refuses any other, at the first hop or at a redirect.
/// Disposing it disposes the handler inside it.
/// </summary>
internal sealed class AllowedOriginsHandler(IReadOnlySet<string> allowedOrigins, HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    internal static readonly HttpRequestOptionsKey<IReadOnlySet<string>> Option = new(nameof(AllowedOriginsHandler));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Options.Set(Option, allowedOrigins);
        return base.SendAsync(request, cancellationToken);
    }
}
