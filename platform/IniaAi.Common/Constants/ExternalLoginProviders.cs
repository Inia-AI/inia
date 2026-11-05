namespace IniaAi.Common.Constants;

/// <summary>
/// Identity external-login provider names. Shared by the WebApi token grants (UserLogins rows)
/// and the WebApp authentication schemes — the values must stay identical so logins linked by
/// either host are recognized by the other.
/// </summary>
public static class ExternalLoginProviders
{
    public const string GOOGLE = "Google";
    public const string OIDC = "OIDC";

    /// <summary>
    /// The token endpoint's grant type for each provider. A module may store the grant type of each
    /// sign-in, so a changed value reads as a new method and can end the account's sessions.
    /// </summary>
    public const string GOOGLE_GRANT_TYPE = "google";

    public const string OIDC_GRANT_TYPE = "oidc";
}
