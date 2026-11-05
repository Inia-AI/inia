using IniaAi.Configuration.Security;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Infrastructure.Persistence.Seed;

/// <summary>
/// The passwords a deployment gives the accounts the first boot creates. The source holds none:
/// a deployment that needs an account created and has no password for it refuses to start. A
/// deployment with password sign-in off creates them with no password, because none could be used.
/// </summary>
public sealed record SeedAccountPasswords(string? SuperAdmin, string? CustomerAdmin, bool PasswordSignInOn = true)
{
    public const string SUPER_ADMIN_PASSWORD_KEY = "Seeding:SuperAdminPassword";
    public const string CUSTOMER_ADMIN_PASSWORD_KEY = "Seeding:CustomerAdminPassword";

    public static SeedAccountPasswords From(IConfiguration configuration)
    {
        return new SeedAccountPasswords(
            configuration[SUPER_ADMIN_PASSWORD_KEY],
            configuration[CUSTOMER_ADMIN_PASSWORD_KEY],
            configuration.GetValue($"{PasswordAuthSettings.SectionName}:{nameof(PasswordAuthSettings.Enabled)}", defaultValue: true));
    }
}
