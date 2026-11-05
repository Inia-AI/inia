using System.Text;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Identity's lookup by email folds lookalikes onto a stored address: a long s finds the account of
/// an s. An address is an account's own only when it matches the stored one in any Unicode form and
/// in any case.
/// </summary>
public static class AccountAddresses
{
    public static bool IsSame(string? storedEmail, string email)
    {
        return string.Equals(
            storedEmail?.Normalize(NormalizationForm.FormC),
            email.Normalize(NormalizationForm.FormC),
            StringComparison.OrdinalIgnoreCase);
    }
}
