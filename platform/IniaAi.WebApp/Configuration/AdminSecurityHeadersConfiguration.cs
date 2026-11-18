using IniaAi.Configuration.Security;
using Microsoft.Net.Http.Headers;

namespace IniaAi.WebApp.Configuration;

/// <summary>
/// Provides the extension method that sets the browser protection headers on the admin app's answers.
/// </summary>
public static class AdminSecurityHeadersConfiguration
{
    private const string NO_SNIFF = "nosniff";
    private const string REFERRER_POLICY = "strict-origin-when-cross-origin";
    private const string REFERRER_POLICY_HEADER = "Referrer-Policy";
    private const string SAME_ORIGIN_FRAMES = "SAMEORIGIN";
    private const string GOOGLE_SIGN_IN_ORIGIN = "https://accounts.google.com";

    // MudBlazor writes its theme into a style element and sets inline styles, so styles stay inline. Scripts do
    // not: every script of the app is a file it serves. A previewed PDF is a blob: frame.
    private const string CONTENT_SECURITY_POLICY =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob:; connect-src 'self'; " +
        "frame-src 'self' blob:; worker-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'self'";
    private const string FORM_ACTION = "form-action 'self'";

    /// <summary>
    /// Adds the middleware that sets nosniff, a referrer policy and a same-origin frame rule on every answer, and the
    /// content policy on every page. The policy's form-action names the sign-in pages of the Google and OIDC sign-in
    /// the deployment turned on, because their sign-in starts from a form. An answer that sets its own policy or
    /// frame rule keeps it. Register it first.
    /// </summary>
    /// <param name="app">The application to add the middleware to.</param>
    /// <returns>The same application.</returns>
    public static WebApplication UseAdminSecurityHeaders(this WebApplication app)
    {
        string contentSecurityPolicy = ContentSecurityPolicy(app.Configuration);

        _ = app.Use((context, next) =>
        {
            HttpResponse response = context.Response;

            response.OnStarting(() =>
            {
                IHeaderDictionary headers = response.Headers;
                headers.XContentTypeOptions = NO_SNIFF;
                headers[REFERRER_POLICY_HEADER] = REFERRER_POLICY;
                if (string.IsNullOrEmpty(headers.XFrameOptions))
                {
                    headers.XFrameOptions = SAME_ORIGIN_FRAMES;
                }

                if (IsPage(response) && string.IsNullOrEmpty(headers.ContentSecurityPolicy))
                {
                    headers.ContentSecurityPolicy = contentSecurityPolicy;
                }

                return Task.CompletedTask;
            });

            return next(context);
        });

        return app;
    }

    private static string ContentSecurityPolicy(IConfiguration configuration)
    {
        return $"{CONTENT_SECURITY_POLICY}; {string.Join(' ', SignInOrigins(configuration).Prepend(FORM_ACTION))}";
    }

    private static IEnumerable<string> SignInOrigins(IConfiguration configuration)
    {
        GoogleAuthSettings google = configuration.GetSection(GoogleAuthSettings.SectionName).Get<GoogleAuthSettings>() ?? new GoogleAuthSettings();
        if (AuthenticationConfiguration.RegistersGoogle(google))
        {
            yield return GOOGLE_SIGN_IN_ORIGIN;
        }

        OidcAuthSettings oidc = configuration.GetSection(OidcAuthSettings.SectionName).Get<OidcAuthSettings>() ?? new OidcAuthSettings();
        if (AuthenticationConfiguration.RegistersOidc(oidc) && Uri.TryCreate(oidc.Issuer, UriKind.Absolute, out Uri? issuer))
        {
            yield return issuer.GetLeftPart(UriPartial.Authority);
        }
    }

    private static bool IsPage(HttpResponse response)
    {
        return response.ContentType?.StartsWith(System.Net.Mime.MediaTypeNames.Text.Html, StringComparison.OrdinalIgnoreCase) == true;
    }
}
