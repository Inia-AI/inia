namespace IniaAi.Application.Common.Services;

/// <summary>
/// Decides whether an outside identity provider vouches for the email it sent. <c>email_verified</c>
/// decides when present. Entra ID sends no such claim; its optional <c>xms_edov</c> claim decides
/// next. With neither claim, the email counts as verified only when the operator says the provider's
/// addresses are ones their organization manages.
/// </summary>
public static class ExternalEmailVerification
{
    public const string EMAIL_VERIFIED_CLAIM = "email_verified";
    public const string ENTRA_DOMAIN_OWNER_VERIFIED_CLAIM = "xms_edov";

    public static bool IsVerified(string? emailVerified, string? xmsEdov, bool trustWithoutClaim)
    {
        if (emailVerified is not null)
        {
            return IsTrue(emailVerified);
        }

        if (xmsEdov is not null)
        {
            return IsTrue(xmsEdov);
        }

        return trustWithoutClaim;
    }

    private static bool IsTrue(string value)
    {
        return value == "1" || (bool.TryParse(value, out bool parsed) && parsed);
    }
}
