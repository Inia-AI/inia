namespace IniaAi.WebApp.Configuration;

/// <summary>
/// The names and the <c>Secure</c> rule of the admin app's cookies, from <see cref="AdminCookieConfiguration.HTTPS_ONLY_KEY"/>.
/// </summary>
public sealed class AdminCookies
{
    private const string HOST_PREFIX = "__Host-";
    private const string SECURE_PREFIX = "__Secure-";
    private const string STATUS_MESSAGE_COOKIE = "Identity.StatusMessage";

    /// <summary>Names the cookies for the deployment's HTTPS rule.</summary>
    /// <param name="httpsOnly">Whether the deployment serves the admin app only over HTTPS.</param>
    public AdminCookies(bool httpsOnly)
    {
        HttpsOnly = httpsOnly;
        StatusMessage = HostCookie(STATUS_MESSAGE_COOKIE);
        StatusMessageCookie = new CookieBuilder
        {
            // Strict is withheld on the redirect that follows an external-IdP callback
            // (the chain originates cross-site), silently dropping login status messages.
            SameSite = SameSiteMode.Lax,
            HttpOnly = true,
            IsEssential = true,
            MaxAge = TimeSpan.FromSeconds(5),
            SecurePolicy = httpsOnly ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest,
        };
    }

    /// <summary>Whether every cookie is <c>Secure</c> and carries a <c>__Host-</c> or <c>__Secure-</c> name.</summary>
    public bool HttpsOnly { get; }

    /// <summary>The name of the cookie that carries a sign-in page's status message across its redirect.</summary>
    public string StatusMessage { get; }

    /// <summary>The options of the status message cookie.</summary>
    public CookieBuilder StatusMessageCookie { get; }

    internal string HostCookie(string name)
    {
        return HttpsOnly ? HOST_PREFIX + name : name;
    }

    internal string SecureCookie(string name)
    {
        return HttpsOnly ? SECURE_PREFIX + name : name;
    }
}
