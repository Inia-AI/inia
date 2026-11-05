using System.Net;

namespace IniaAi.Common.Http;

/// <summary>
/// Follows redirects itself, so each hop meets the guard: a direct hop when its connection opens,
/// and a hop a proxy carries, which the connection check cannot see, before it is sent. A refusal
/// the HTTP stack wrapped at connect time is unwrapped, so no caller retries it as a network
/// error. Redirects follow the rules of the built-in .NET handler: no step down from HTTPS to
/// HTTP, a POST becomes a GET after 301, 302 and 300, every method but HEAD becomes a GET after
/// 303, and the Authorization header is dropped.
/// </summary>
internal sealed class CheckedRedirectHandler : DelegatingHandler
{
    private const int MAX_REDIRECTS = 50;

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
}
