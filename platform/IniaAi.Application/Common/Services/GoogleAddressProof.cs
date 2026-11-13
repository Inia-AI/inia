namespace IniaAi.Application.Common.Services;

/// <summary>
/// Whether a Google token proves who holds its address today. Google is authoritative for a Gmail
/// address and for a Google Workspace account, whose token carries the organization's domain in its
/// <c>hd</c> claim. For any other address the verified flag only says Google checked the address when
/// the account was made: it may have changed hands since.
/// </summary>
public static class GoogleAddressProof
{
    public const string HOSTED_DOMAIN_CLAIM = "hd";

    private static readonly string[] _gmailDomains = ["gmail.com", "googlemail.com"];

    /// <summary>
    /// Whether Google vouches for <paramref name="email"/> in both apps' Google sign-in: it marks the
    /// address verified and, with <paramref name="provenAddressesOnly"/> on, also proves it.
    /// </summary>
    public static bool Vouches(bool emailVerified, string? email, string? hostedDomain, bool provenAddressesOnly)
    {
        return emailVerified && (!provenAddressesOnly || Proves(email, hostedDomain));
    }

    public static bool Proves(string? email, string? hostedDomain)
    {
        if (!string.IsNullOrEmpty(hostedDomain))
        {
            return true;
        }

        if (email is null)
        {
            return false;
        }

        int at = email.LastIndexOf('@');
        return at >= 0 && _gmailDomains.Contains(email[(at + 1)..], StringComparer.OrdinalIgnoreCase);
    }
}
