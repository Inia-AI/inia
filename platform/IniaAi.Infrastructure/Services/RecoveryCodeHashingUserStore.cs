using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Identity's user store, keeping two-factor recovery codes as salted password hashes instead of in
/// clear. A code has about 47 bits of entropy, under the 112 bits below which ASVS 6.5.2 asks for a
/// password hash rather than a plain one. Redeeming checks the code against each stored hash. A code
/// stored in clear before this store holds a hyphen, which a hash never does; it is compared as it is,
/// and the user's remaining codes are stored hashed from then on.
/// </summary>
public sealed class RecoveryCodeHashingUserStore(UserDataDbContext context, IdentityErrorDescriber? describer = null)
    : UserStore<User, IdentityRole, UserDataDbContext>(context, describer)
{
    internal const string INTERNAL_LOGIN_PROVIDER = "[AspNetUserStore]";
    internal const string RECOVERY_CODE_TOKEN_NAME = "RecoveryCodes";
    private const char SEPARATOR = ';';
    private const char CODE_IN_CLEAR_MARK = '-';

    public override Task ReplaceCodesAsync(User user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken = default)
    {
        return StoreAsync(user, recoveryCodes.Select(code => PasswordHashing.Hasher.HashPassword(user, code)), cancellationToken);
    }

    public override async Task<bool> RedeemCodeAsync(User user, string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrEmpty(code);

        string merged = await GetTokenAsync(user, INTERNAL_LOGIN_PROVIDER, RECOVERY_CODE_TOKEN_NAME, cancellationToken) ?? "";
        List<string> stored = [.. merged.Split(SEPARATOR, StringSplitOptions.RemoveEmptyEntries)];
        int match = stored.FindIndex(entry => Matches(user, entry, code));
        if (match < 0)
        {
            return false;
        }

        stored.RemoveAt(match);
        await StoreAsync(user, stored.Select(entry => IsInClear(entry) ? PasswordHashing.Hasher.HashPassword(user, entry) : entry), cancellationToken);
        return true;
    }

    private static bool Matches(User user, string entry, string code)
    {
        return IsInClear(entry)
            ? string.Equals(entry, code, StringComparison.Ordinal)
            : PasswordHashing.Hasher.VerifyHashedPassword(user, entry, code) != PasswordVerificationResult.Failed;
    }

    private static bool IsInClear(string entry)
    {
        return entry.Contains(CODE_IN_CLEAR_MARK, StringComparison.Ordinal);
    }

    private Task StoreAsync(User user, IEnumerable<string> entries, CancellationToken cancellationToken)
    {
        return SetTokenAsync(user, INTERNAL_LOGIN_PROVIDER, RECOVERY_CODE_TOKEN_NAME, string.Join(SEPARATOR, entries), cancellationToken);
    }
}
