namespace IniaAi.Configuration.Security;

/// <summary>
/// Configuration settings for OIDC authentication (supports any OIDC provider: Okta, Auth0, Azure AD, etc.).
/// </summary>
public class OidcAuthSettings
{
    /// <summary>
    /// The section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Authentication:Oidc";

    /// <summary>
    /// Gets or sets the OAuth Client ID (used as audience for token validation).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth Client Secret. Optional: when set, the admin app's
    /// server-side OIDC flow authenticates as a confidential client; when empty,
    /// it runs as a public client using PKCE only.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OIDC authorization server issuer URL.
    /// This is the full URL including the auth server path.
    /// Examples: "https://dev-123456.okta.com/oauth2/default", "https://company.auth0.com/", "https://login.microsoftonline.com/{tenant}/v2.0".
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether OIDC authentication is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the display name shown on the SSO button (e.g. "Okta", "Auth0", "Company SSO").
    /// </summary>
    public string DisplayName { get; set; } = "SSO";

    /// <summary>
    /// Gets or sets the allowed email domains for OIDC users.
    /// If empty, all domains are allowed.
    /// Example: ["company.com", "partner.com"]
    /// </summary>
    public List<string> AllowedDomains { get; set; } = [];

    /// <summary>
    /// Trusts the email of a provider that sends neither <c>email_verified</c> nor <c>xms_edov</c>.
    /// Set it only when the organization itself manages every address at the provider, such as a
    /// single-tenant Entra ID. Off by default: such a sign-in then matches no account.
    /// </summary>
    public bool TrustUnverifiedEmail { get; set; }
}
