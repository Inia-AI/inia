using IniaAi.WebApi.Extensions;
using Microsoft.Net.Http.Headers;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Provides the extension method that sets the browser protection headers on the API's answers.
/// </summary>
public static class SecurityHeadersConfiguration
{
    private const string CONTENT_SECURITY_POLICY = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    private const string NO_SNIFF = "nosniff";
    // Kept beside the policy's frame-ancestors for browsers that read only the older header.
    private const string FRAME_OPTIONS = "DENY";
    private const string REFERRER_POLICY = "strict-origin-when-cross-origin";
    private const string REFERRER_POLICY_HEADER = "Referrer-Policy";
    private const string API_DOCUMENTATION_PATH = "/swagger";

    /// <summary>
    /// Adds the middleware that sets the browser protection headers on every answer, the ones a later middleware
    /// writes by itself included (a refusal, a rate-limit answer, a preflight, a failure). Register it first, after
    /// the forwarded headers. The headers are set as the answer starts, so an answer that sets its own cache rule or
    /// its own policy (a file preview's <c>sandbox</c>) keeps it. The Swagger UI, served on a developer machine only,
    /// gets no policy: it loads its own scripts.
    /// </summary>
    /// <param name="app">The application to add the middleware to.</param>
    /// <returns>The same application.</returns>
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        bool servesApiDocumentation = app.Environment.ServesApiDocumentation();

        _ = app.Use((context, next) =>
        {
            HttpResponse response = context.Response;
            bool isApiDocumentation = servesApiDocumentation && context.Request.Path.StartsWithSegments(API_DOCUMENTATION_PATH);

            response.OnStarting(() =>
            {
                IHeaderDictionary headers = response.Headers;
                headers.XContentTypeOptions = NO_SNIFF;
                headers.XFrameOptions = FRAME_OPTIONS;
                headers[REFERRER_POLICY_HEADER] = REFERRER_POLICY;
                if (!isApiDocumentation && string.IsNullOrEmpty(headers.ContentSecurityPolicy))
                {
                    headers.ContentSecurityPolicy = CONTENT_SECURITY_POLICY;
                }

                if (string.IsNullOrEmpty(headers.CacheControl))
                {
                    headers.CacheControl = CacheControlHeaderValue.NoStoreString;
                }

                return Task.CompletedTask;
            });

            return next(context);
        });

        return app;
    }
}
