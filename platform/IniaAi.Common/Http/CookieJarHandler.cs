using System.Net;

namespace IniaAi.Common.Http;

/// <summary>
/// Names one caller's cookie jar on every request it passes on, so that caller's requests share
/// cookies through a handler that other callers share too. <see cref="HopCookieHandler"/>, below
/// the redirect loop, sends and keeps the cookies. Disposing it disposes the handler inside it.
/// </summary>
internal sealed class CookieJarHandler(CookieContainer cookies, HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    internal static readonly HttpRequestOptionsKey<CookieContainer> JarOption = new(nameof(CookieJarHandler));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Options.Set(JarOption, cookies);
        return base.SendAsync(request, cancellationToken);
    }
}
