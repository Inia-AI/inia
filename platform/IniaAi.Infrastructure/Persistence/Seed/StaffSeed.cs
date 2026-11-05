using IniaAi.Configuration.Extensions;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Infrastructure.Persistence.Seed;

/// <summary>
/// An Inia staff member that every start makes sure exists, with at least these roles. <paramref name="Key"/>
/// is the entry's configuration key, which a refusal names instead of the address.
/// </summary>
public sealed record StaffAccount(string Email, IReadOnlyList<string> Roles, string? Key = null);

/// <summary>
/// The staff accounts a deployment seeds, and whether it disables the seeded super admin. A
/// deployment that signs in only through an identity provider has no use for the super admin, so
/// its staff come from configuration and sign in with their own provider accounts.
/// </summary>
public sealed record StaffSeed(IReadOnlyList<StaffAccount> Accounts, bool DisablesSuperAdmin = false)
{
    public const string STAFF_KEY = "Seeding:Staff";
    public const string DISABLE_SUPER_ADMIN_KEY = "Seeding:DisableSuperAdmin";

    public static StaffSeed From(IConfiguration configuration)
    {
        if (!string.IsNullOrEmpty(configuration[STAFF_KEY]))
        {
            throw new SeedConfigurationException(
                $"{STAFF_KEY} is set as one value. Set each entry as indexed keys, e.g. Seeding__Staff__0__Email and Seeding__Staff__0__Roles__0.");
        }

        List<StaffAccount> accounts = [.. configuration.GetSection(STAFF_KEY)
            .GetChildren()
            .Select(entry => new StaffAccount(
                entry["Email"] ?? throw new SeedConfigurationException($"{entry.Path}:Email is not set."),
                RolesOf(configuration, $"{entry.Path}:Roles"),
                entry.Path))];

        return new StaffSeed(accounts, DisablesSuperAdminFrom(configuration));
    }

    private static bool DisablesSuperAdminFrom(IConfiguration configuration)
    {
        string? value = configuration[DISABLE_SUPER_ADMIN_KEY];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value.Trim(), out bool disables)
            ? disables
            : throw new SeedConfigurationException($"{DISABLE_SUPER_ADMIN_KEY} must be true or false.");
    }

    private static string[] RolesOf(IConfiguration configuration, string key)
    {
        try
        {
            return configuration.GetList(key);
        }
        catch (InvalidOperationException oneValue)
        {
            throw new SeedConfigurationException(oneValue.Message);
        }
    }
}
