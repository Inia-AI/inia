using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Persistence.Seed;

/// <summary>
/// Resolves the secret shared by every <c>service_user_&lt;org&gt;</c> OAuth client. The source
/// carries no default: a deployment that sets none gets the system users but no client, and the
/// next boot with a secret adds it.
/// </summary>
public static class SystemUserClientSecret
{
    public const string CONFIGURATION_KEY = "OpenIddict:ClientSecret";

    /// <summary>
    /// Returns the secret to seed, or <c>null</c> when the key is missing, empty or whitespace,
    /// in which case the caller must not seed a client at all.
    /// </summary>
    public static string? Resolve(IConfiguration configuration, ILogger logger)
    {
        string? configured = configuration[CONFIGURATION_KEY];

        if (string.IsNullOrWhiteSpace(configured))
        {
            logger.LogWarning(
                "{ConfigurationKey} is not set, so no service_user_<org> OAuth client is seeded and client-credentials sign-in is off until it is",
                CONFIGURATION_KEY);
            return null;
        }

        return configured;
    }
}
