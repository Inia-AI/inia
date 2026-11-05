using System.Net;

namespace IniaAi.Common.Http;

/// <summary>
/// Sends and keeps cookies on each redirect hop, in the jar that <see cref="CookieJarHandler"/>
/// named for the request: the jar's cookies for that hop's URI join any Cookie header the caller
/// set, for that hop only. A request without a jar sends and keeps no cookies.
/// </summary>
internal sealed class HopCookieHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private const string COOKIE_HEADER = "Cookie";
    private const string SET_COOKIE_HEADER = "Set-Cookie";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(CookieJarHandler.JarOption, out CookieContainer? jar)
            || request.RequestUri is not Uri target)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        string jarCookies = jar.GetCookieHeader(target);
        HttpResponseMessage response;

        if (jarCookies.Length == 0)
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        else
        {
            string[] callerCookies = request.Headers.TryGetValues(COOKIE_HEADER, out IEnumerable<string>? values) ? [.. values] : [];
            _ = request.Headers.TryAddWithoutValidation(COOKIE_HEADER, jarCookies);

            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                _ = request.Headers.Remove(COOKIE_HEADER);

                if (callerCookies.Length > 0)
                {
                    _ = request.Headers.TryAddWithoutValidation(COOKIE_HEADER, callerCookies);
                }
            }
        }

        if (response.Headers.TryGetValues(SET_COOKIE_HEADER, out IEnumerable<string>? setCookies))
        {
            foreach (string setCookie in setCookies)
            {
                try
                {
                    jar.SetCookies(target, setCookie);
                }
                catch (CookieException)
                {
                    // A malformed cookie is dropped; it must not fail the request that carried it.
                }
            }
        }

        return response;
    }
}
