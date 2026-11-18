using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApp.Configuration;

/// <summary>
/// Makes every cookie of the admin app <c>Secure</c> with a <c>__Host-</c> or <c>__Secure-</c> name when the
/// deployment serves the admin app only over HTTPS (<see cref="HTTPS_ONLY_KEY"/>). Off by default: a browser refuses
/// a <c>__Host-</c> cookie on plain HTTP, <c>http://localhost</c> included, and the antiforgery check refuses every
/// plain-HTTP request once its cookie must be <c>Secure</c>.
/// </summary>
public static class AdminCookieConfiguration
{
    /// <summary>The setting that turns on <c>Secure</c> cookies with <c>__Host-</c> and <c>__Secure-</c> names.</summary>
    public const string HTTPS_ONLY_KEY = "AdminApp:HttpsOnlyCookies";

    private const string ANTIFORGERY_COOKIE = "Antiforgery";
    private const string CORRELATION_COOKIE = "Correlation.";
    private const string NONCE_COOKIE = "OpenIdConnect.Nonce.";

    /// <summary>
    /// Registers <see cref="AdminCookies"/> and, with <see cref="HTTPS_ONLY_KEY"/> on, renames and secures the cookie
    /// of every sign-in scheme and the antiforgery, Google and OIDC cookies. Call it after the authentication setup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration that holds <see cref="HTTPS_ONLY_KEY"/>.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddAdminCookieProtection(this IServiceCollection services, IConfiguration configuration)
    {
        AdminCookies cookies = new(configuration.GetValue<bool>(HTTPS_ONLY_KEY));
        _ = services.AddSingleton(cookies);
        if (!cookies.HttpsOnly)
        {
            return services;
        }

        _ = services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>>(new SecureSignInCookies(cookies));
        _ = services.PostConfigure<AntiforgeryOptions>(options => Secure(options.Cookie, cookies.HostCookie(ANTIFORGERY_COOKIE)));
        _ = services.PostConfigureAll<GoogleOptions>(options => Secure(options.CorrelationCookie, cookies.SecureCookie(CORRELATION_COOKIE)));
        _ = services.PostConfigureAll<OpenIdConnectOptions>(options =>
        {
            Secure(options.CorrelationCookie, cookies.SecureCookie(CORRELATION_COOKIE));
            Secure(options.NonceCookie, cookies.SecureCookie(NONCE_COOKIE));
        });

        return services;
    }

    private static void Secure(CookieBuilder cookie, string name)
    {
        cookie.Name = name;
        cookie.SecurePolicy = CookieSecurePolicy.Always;
    }

    private sealed class SecureSignInCookies(AdminCookies cookies) : IPostConfigureOptions<CookieAuthenticationOptions>
    {
        public void PostConfigure(string? name, CookieAuthenticationOptions options)
        {
            if (!string.IsNullOrEmpty(name))
            {
                Secure(options.Cookie, cookies.HostCookie(name));
            }
        }
    }
}
