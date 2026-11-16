using System.Net;

namespace IniaAi.Common.Http;

/// <summary>
/// Follows redirects itself, so each hop meets the guard: a direct hop when its connection opens,
/// and a hop a proxy carries, which the connection check cannot see, before it is sent. A refusal
/// the HTTP stack wrapped at connect time is unwrapped, so no caller retries it as a network
/// error. Redirects follow the rules of the built-in .NET handler: no step down from HTTPS to
/// HTTP, a POST becomes a GET after 301, 302 and 300, every method but HEAD becomes a GET after
/// 303, and the Authorization header is dropped. A redirect to another scheme, host or port also
/// drops every header the caller set but Accept, since a header can carry a stored key the next
/// host must not receive. A caller that named the web addresses it may reach is refused any other
/// at every hop.
/// </summary>
internal sealed class CheckedRedirectHandler : DelegatingHandler
{
    private const int MAX_REDIRECTS = 50;
    private const string ACCEPT_HEADER = "Accept";

    private readonly OutboundAddressGuard _guard;

    public CheckedRedirectHandler(OutboundAddressGuard guard, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _guard = guard;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (int redirects = 0; ; redirects++)
        {
            Uri target = request.RequestUri
                ?? throw new InvalidOperationException("The request has no URI.");

            RefuseOutsideAllowedOrigins(request, target);
            await _guard.CheckProxiedTargetAsync(target, cancellationToken);

            HttpResponseMessage response = await SendCheckedAsync(request, cancellationToken);

            if (redirects == MAX_REDIRECTS || RedirectTarget(target, response) is not Uri next)
            {
                return response;
            }

            HttpStatusCode status = response.StatusCode;
            response.Dispose();
            PrepareRedirect(request, status, next);
        }
    }

    private static void RefuseOutsideAllowedOrigins(HttpRequestMessage request, Uri target)
    {
        if (request.Options.TryGetValue(AllowedOriginsHandler.Option, out IReadOnlySet<string>? allowed)
            && OutboundOrigin.Of(target) is var origin
            && !allowed.Contains(origin))
        {
            throw new OutboundOriginRefusedException(origin);
        }
    }

    private async Task<HttpResponseMessage> SendCheckedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (OutboundAddressRefusedException.FindIn(ex) is OutboundAddressRefusedException refusal)
        {
            throw refusal;
        }
    }

    private static Uri? RedirectTarget(Uri target, HttpResponseMessage response)
    {
        if (!IsRedirect(response.StatusCode) || response.Headers.Location is not Uri location)
        {
            return null;
        }

        Uri next = location.IsAbsoluteUri ? location : new Uri(target, location);

        if (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return target.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp ? null : next;
    }

    private static bool IsRedirect(HttpStatusCode status)
    {
        return status is HttpStatusCode.MultipleChoices
            or HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private static void PrepareRedirect(HttpRequestMessage request, HttpStatusCode status, Uri next)
    {
        if (request.RequestUri is { } previous && !OutboundOrigin.Same(previous, next))
        {
            foreach (string name in request.Headers.Select(header => header.Key).Where(IsNotAccept).ToList())
            {
                _ = request.Headers.Remove(name);
            }
        }

        request.RequestUri = next;
        request.Headers.Authorization = null;

        bool becomesGet = status switch
        {
            HttpStatusCode.MultipleChoices or HttpStatusCode.MovedPermanently or HttpStatusCode.Found =>
                request.Method == HttpMethod.Post,
            HttpStatusCode.SeeOther => request.Method != HttpMethod.Get && request.Method != HttpMethod.Head,
            _ => false,
        };

        if (becomesGet)
        {
            request.Method = HttpMethod.Get;
            request.Content = null;
            request.Headers.TransferEncodingChunked = false;
        }
    }

    private static bool IsNotAccept(string headerName)
    {
        return !string.Equals(headerName, ACCEPT_HEADER, StringComparison.OrdinalIgnoreCase);
    }
}
