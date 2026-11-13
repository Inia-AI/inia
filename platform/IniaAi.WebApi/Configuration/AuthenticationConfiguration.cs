using Finbuckle.MultiTenant.Extensions;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Constants;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.MultiTenant;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// No-op OIDC token validator used when OIDC authentication is disabled.
/// Prevents DI resolution failures without eagerly connecting to an OIDC provider.
/// </summary>
internal sealed class DisabledOidcTokenValidator : IOidcTokenValidator
{
    public Task<OidcTokenValidationResult> ValidateTokenAsync(string idToken, CancellationToken ct = default)
    {
        return Task.FromResult(new OidcTokenValidationResult(
            IsValid: false, Error: "OIDC authentication is not configured."));
    }
}

/// <summary>
/// Lets the token server accept every grant type a module registered with
/// <see cref="CustomGrants.AddCustomGrant{THandler}"/>, whichever order the registrations ran in.
/// </summary>
internal sealed class CustomGrantFlows(IEnumerable<CustomGrantType> grantTypes) : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        foreach (CustomGrantType grantType in grantTypes)
        {
            _ = options.GrantTypes.Add(grantType.Name);
        }
    }
}

/// <summary>
/// Provides extension methods to configure JWT and OpenIddict-based authentication.
/// </summary>
public static class AuthenticationConfiguration
{
    private const int TOKEN_ENCRYPTION_KEY_BYTES = 32;
    private const string TOKEN_ENCRYPTION_KEY_PURPOSE = "IniaAi token encryption";

    /// <summary>
    /// Configures OpenIddict token server, JWT validation, and IdentityCore for user authentication.
    /// </summary>
    /// <param name="services">The service collection to add authentication services to.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        JwtSettings jwtTokenSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("Jwt settings are not configured");
        Validator.ValidateObject(
            jwtTokenSettings, new ValidationContext(jwtTokenSettings), validateAllProperties: true);

        // Get password auth settings for lockout configuration
        PasswordAuthSettings passwordSettings = configuration
            .GetSection(PasswordAuthSettings.SectionName)
            .Get<PasswordAuthSettings>() ?? new PasswordAuthSettings();

        // Add Identity (no UI)
        _ = services.AddIdentityCore<User>(options =>
        {
            options.SignIn.RequireConfirmedAccount = false;
            PasswordPolicy.Apply(options.Password);

            // Configure account lockout for brute force protection
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(passwordSettings.LockoutDurationMinutes);
            options.Lockout.MaxFailedAccessAttempts = passwordSettings.MaxFailedAccessAttempts;
        })
        .AddRoles<IdentityRole>()
        .AddPlatformUserStores()
        .AddDefaultTokenProviders();

        // Configure OpenIddict
        _ = services.AddOpenIddict()
            .AddCore(options =>
            {
                _ = options.UseEntityFrameworkCore()
                       .UseDbContext<UserDataDbContext>();

            })
            .AddServer(options =>
            {
                // Register token endpoints (main and MFA)
                _ = options.SetTokenEndpointUris(
                    "/api/v1/connect/token",
                    "/api/v1/connect/token/mfa");
                _ = options.SetRevocationEndpointUris("/api/v1/connect/revoke");
                _ = options.AddEventHandler(SignOutEndsTheSession.Descriptor);
                foreach (OpenIddictServerHandlerDescriptor descriptor in RefusedRefreshAudit.Descriptors)
                {
                    _ = options.AddEventHandler(descriptor);
                }

                _ = options.AllowPasswordFlow();
                _ = options.AllowClientCredentialsFlow();
                _ = options.AllowRefreshTokenFlow();
                _ = options.AllowCustomFlow("mfa");
                _ = options.AllowCustomFlow(ExternalLoginProviders.GOOGLE_GRANT_TYPE);
                _ = options.AllowCustomFlow(ExternalLoginProviders.OIDC_GRANT_TYPE);

                _ = options.SetAccessTokenLifetime(jwtTokenSettings.AccessTokenLifetime);
                _ = options.SetRefreshTokenLifetime(jwtTokenSettings.RefreshTokenLifetime);

                _ = options.AcceptAnonymousClients();

                _ = options.AddEncryptionKey(TokenEncryptionKey(jwtTokenSettings.SecretKey));
                _ = options.AddEphemeralSigningKey();
                _ = options.DisableAccessTokenEncryption();

                var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtTokenSettings.SecretKey));
                _ = options.AddSigningKey(signingKey);

                _ = options.SetIssuer(new Uri(jwtTokenSettings.Issuer));

                _ = options.UseAspNetCore()
                       .EnableTokenEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                _ = options.UseLocalServer();
                _ = options.UseAspNetCore();

                // Revoking a user's tokens must end their access at once, not when the token expires.
                _ = options.EnableTokenEntryValidation();

                // Configure role claim type to use "role" instead of the long schema URL
                _ = options.Configure(o => o.TokenValidationParameters.RoleClaimType = Claims.Role);
            });

        _ = services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>, CustomGrantFlows>();
        services.TryAddSingleton(TimeProvider.System);

        // Configure authentication schemes - OpenIddict handles token validation
        // Google Sign-In is handled by frontend using Google Identity Services SDK
        // Backend only validates Google ID tokens, no OAuth callback flow needed
        _ = services.AddAuthentication(options =>
        {
            options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        // SSL is terminated at the reverse proxy (ALB/Caddy), so the app always receives HTTP
        _ = services.Configure<OpenIddictServerAspNetCoreOptions>(options =>
        {
            options.DisableTransportSecurityRequirement = true;
        });

        // Register OIDC token validator only when OIDC auth is enabled
        OidcAuthSettings oidcSettings = configuration
            .GetSection(OidcAuthSettings.SectionName)
            .Get<OidcAuthSettings>() ?? new OidcAuthSettings();
        if (oidcSettings.Enabled && !string.IsNullOrEmpty(oidcSettings.Issuer))
        {
            _ = services.AddSingleton<IOidcTokenValidator, OidcTokenValidator>();
        }
        else
        {
            // Register a no-op validator — controller guards with Enabled check before calling this
            _ = services.AddSingleton<IOidcTokenValidator>(
                new DisabledOidcTokenValidator());
        }

        // Configure Finbuckle.MultiTenant with distributed caching
        _ = services.AddMultiTenant<OrganizationTenantInfo>()
            .WithStrategy<HeaderTenantStrategy>(ServiceLifetime.Scoped) // Custom header validation strategy
            .WithStore<OrganizationTenantStore>(ServiceLifetime.Scoped)
            .WithDistributedCacheStore(TimeSpan.FromMinutes(10)); // Cache tenant lookups for 10 minutes

        return services;
    }

    /// <summary>
    /// The key that encrypts refresh tokens, derived from <c>Jwt:SecretKey</c> rather than made at
    /// start, so a refresh token outlives a restart and every instance of the API can read it.
    /// Rotating the secret ends every session.
    /// </summary>
    private static SymmetricSecurityKey TokenEncryptionKey(string secretKey)
    {
        return new SymmetricSecurityKey(HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(secretKey),
            TOKEN_ENCRYPTION_KEY_BYTES,
            info: Encoding.UTF8.GetBytes(TOKEN_ENCRYPTION_KEY_PURPOSE)));
    }
}
