
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.Setup;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.Infrastructure.Persistence.Seed;

public static class SystemUserSeeder
{
    private const string CLIENT_ID_PREFIX = "service_user_";
    private const string EMAIL_DOMAIN = "@inia.ai";

    /// <summary>
    /// Creates a system user for each organization the <see cref="ISystemUserPolicy"/> names, if not
    /// already present.
    /// </summary>
    public static async Task SeedForAllOrganizationsAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        using IServiceScope scope = serviceProvider.CreateScope();
        UserDataDbContext dbContext = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        UserManager<User> userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        IOpenIddictApplicationManager manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        ISystemUserPolicy policy = scope.ServiceProvider.GetService<ISystemUserPolicy>() ?? new SystemUserForEveryOrganization();
        ILogger logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(SystemUserSeeder));

        string? clientSecret = SystemUserClientSecret.Resolve(configuration, logger);

        using DatabaseSessionLock? startupLock = StartupDatabaseLock.Acquire(dbContext, logger);

        List<Organization> orgs = [.. (await dbContext.Organizations.ToListAsync()).Where(policy.SeedsSystemUser)];

        foreach (Organization? org in orgs)
        {
            string clientId = GetClientId(org);
            try
            {
                await SeedOrganizationAsync(userManager, manager, org, clientId, clientSecret);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One organization's bad state (e.g. deleted between the list query and
                // the insert, leaving an FK violation) must not prevent the application
                // from starting; the next boot retries it.
                logger.LogError(ex,
                    "Failed to provision system user {ClientId} for organization {OrganizationId}; continuing startup",
                    clientId,
                    org.Id);
            }
        }

        await EnsureApiScopeAsync(scope.ServiceProvider);
    }

    /// <summary>
    /// The client id doubles as the system user's user name.
    /// </summary>
    public static string GetClientId(Organization org)
    {
        return $"{CLIENT_ID_PREFIX}{org.Id}".ToLower();
    }

    private static async Task SeedOrganizationAsync(
        UserManager<User> userManager,
        IOpenIddictApplicationManager manager,
        Organization org,
        string clientId,
        string? clientSecret)
    {
        string email = $"{CLIENT_ID_PREFIX}{org.Id}{EMAIL_DOMAIN}";

        User? existing = await userManager.FindByEmailAsync(email);
        User systemUser = existing ?? await CreateSystemUserAsync(userManager, org, clientId, email);

        if (systemUser.PasswordHash is not null)
        {
            _ = await userManager.RemovePasswordAsync(systemUser);
        }

        if (clientSecret is null)
        {
            // Seeding without a secret would leave a public client behind, which is the
            // state this seeder exists to repair; the user is still provisioned so the
            // application can be added on the next boot once the secret is configured.
            return;
        }

        // Always check and update OpenIddict application permissions
        object? appByClient = await manager.FindByClientIdAsync(clientId);

        if (appByClient is null)
        {
            _ = await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
                ClientType = ClientTypes.Confidential,
                Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.GrantTypes.Password,
                        Permissions.GrantTypes.RefreshToken,
                        Permissions.Prefixes.GrantType + "mfa",
                        Permissions.Prefixes.GrantType + "google",
                        Permissions.Prefixes.GrantType + "oidc",
                        Permissions.Prefixes.Scope + "offline_access",
                        Permissions.Prefixes.Scope + "api"
                    },
                Properties =
                    {
                        ["user_id"] = CreateJsonElement(systemUser.Id.ToString()),
                        ["organization_id"] = CreateJsonElement(systemUser.OrganizationId.ToString())
                    }
            });
        }
        else
        {
            var descriptor = new OpenIddictApplicationDescriptor();
            await manager.PopulateAsync(descriptor, appByClient);

            // A client stored as public (seeded before a secret was configured) keeps that
            // type through PopulateAsync, and OpenIddict refuses a secret on a public
            // client; assigning the type outright converts it.
            descriptor.ClientType = ClientTypes.Confidential;
            descriptor.ClientId = clientId;
            descriptor.ClientSecret = clientSecret;

            string offlineAccessPermission = Permissions.Prefixes.Scope + "offline_access";
            if (!descriptor.Permissions.Contains(offlineAccessPermission))
            {
                _ = descriptor.Permissions.Add(offlineAccessPermission);
            }

            if (!descriptor.Permissions.Contains(Permissions.Endpoints.Token))
            {
                _ = descriptor.Permissions.Add(Permissions.Endpoints.Token);
            }

            if (!descriptor.Permissions.Contains(Permissions.GrantTypes.ClientCredentials))
            {
                _ = descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
            }

            if (!descriptor.Permissions.Contains(Permissions.GrantTypes.RefreshToken))
            {
                _ = descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
            }

            if (!descriptor.Permissions.Contains(Permissions.GrantTypes.Password))
            {
                _ = descriptor.Permissions.Add(Permissions.GrantTypes.Password);
            }

            string mfaGrantPermission = Permissions.Prefixes.GrantType + "mfa";
            if (!descriptor.Permissions.Contains(mfaGrantPermission))
            {
                _ = descriptor.Permissions.Add(mfaGrantPermission);
            }

            string googleGrantPermission = Permissions.Prefixes.GrantType + "google";
            if (!descriptor.Permissions.Contains(googleGrantPermission))
            {
                _ = descriptor.Permissions.Add(googleGrantPermission);
            }

            string oidcGrantPermission = Permissions.Prefixes.GrantType + "oidc";
            if (!descriptor.Permissions.Contains(oidcGrantPermission))
            {
                _ = descriptor.Permissions.Add(oidcGrantPermission);
            }

            if (!descriptor.Permissions.Contains(Permissions.Prefixes.Scope + "api"))
            {
                _ = descriptor.Permissions.Add(Permissions.Prefixes.Scope + "api");
            }

            await manager.UpdateAsync(appByClient, descriptor);
        }
    }

    private static async Task EnsureApiScopeAsync(IServiceProvider scopedProvider)
    {
        IOpenIddictScopeManager scopeManager = scopedProvider.GetRequiredService<IOpenIddictScopeManager>();

        if (await scopeManager.FindByNameAsync("api") is null)
        {
            _ = await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "api",
                DisplayName = "Inia API access",
                Resources = { "resource-server-name" } // optional: for audience/resource mapping
            });
        }
    }

    /// <summary>
    /// Multiple app instances can boot concurrently (deployments, integration-test
    /// fixtures), each running this seeder over the same organizations. Losing the
    /// creation race — IdentityResult duplicate errors or the database unique index
    /// firing mid-save — means the user now exists, which is the seeder's goal, so the
    /// winner's row is used instead of failing the entire application start.
    /// </summary>
    private static async Task<User> CreateSystemUserAsync(
        UserManager<User> userManager,
        Organization org,
        string userName,
        string email)
    {
        var candidate = new User
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            IsSystem = true,
            OrganizationId = org.Id,
            UserOrganizations =
            [
                new() {
                    OrganizationId = org.Id,
                    IsDefault = true
                }
            ]
        };

        IdentityResult result;
        try
        {
            result = await userManager.CreateAsync(candidate);
        }
        catch (DbUpdateException)
        {
            User? raceWinner = await FindExistingSystemUserAsync(userManager, userName, email);
            if (raceWinner is not null)
            {
                return raceWinner;
            }

            throw;
        }

        if (result.Succeeded)
        {
            return candidate;
        }

        User? winner = await FindExistingSystemUserAsync(userManager, userName, email);
        if (winner is not null)
        {
            return winner;
        }

        string errors = string.Join(", ", result.Errors.Select(e => e.Description));
        throw new Exception($"Failed to create system user for org '{org.Name}': {errors}");
    }

    private static async Task<User?> FindExistingSystemUserAsync(
        UserManager<User> userManager,
        string userName,
        string email)
    {
        return await userManager.FindByEmailAsync(email)
            ?? await userManager.FindByNameAsync(userName);
    }

    private static JsonElement CreateJsonElement(string value)
    {
        using var doc = JsonDocument.Parse($"\"{value}\"");
        return doc.RootElement.Clone();
    }
}
