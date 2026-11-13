using IniaAi.Application.Common.Services;
using IniaAi.Common.Constants;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace IniaAi.WebApp.Configuration;

/// <summary>
/// Configures cookie-based Identity authentication for the WebApp, including the
/// config-gated Google and OIDC external-login schemes.
/// </summary>
public static class AuthenticationConfiguration
{
    /// <summary>
    /// Registers Identity cookies, Identity core services, and — when enabled and fully
    /// configured — the "Google" and "OIDC" external authentication schemes. The scheme
    /// names must match the provider names the WebApi writes to UserLogins
    /// (<see cref="ExternalLoginProviders"/>) so links interoperate between hosts.
    /// </summary>
    public static IServiceCollection AddWebAppAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        _ = services.Configure<GoogleAuthSettings>(configuration.GetSection(GoogleAuthSettings.SectionName));
        _ = services.Configure<OidcAuthSettings>(configuration.GetSection(OidcAuthSettings.SectionName));
        _ = services.Configure<PasswordAuthSettings>(configuration.GetSection(PasswordAuthSettings.SectionName));

        GoogleAuthSettings googleSettings = configuration.GetSection(GoogleAuthSettings.SectionName).Get<GoogleAuthSettings>() ?? new GoogleAuthSettings();
        OidcAuthSettings oidcSettings = configuration.GetSection(OidcAuthSettings.SectionName).Get<OidcAuthSettings>() ?? new OidcAuthSettings();
        PasswordAuthSettings passwordSettings = configuration.GetSection(PasswordAuthSettings.SectionName).Get<PasswordAuthSettings>() ?? new PasswordAuthSettings();

        AuthenticationBuilder authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });
        _ = authBuilder.AddIdentityCookies();

        if (googleSettings.Enabled && !string.IsNullOrEmpty(googleSettings.ClientId) && !string.IsNullOrEmpty(googleSettings.ClientSecret))
        {
            _ = authBuilder.AddGoogle(ExternalLoginProviders.GOOGLE, options =>
            {
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.ClientId = googleSettings.ClientId;
                options.ClientSecret = googleSettings.ClientSecret;
                options.ClaimActions.MapJsonKey(ExternalEmailVerification.EMAIL_VERIFIED_CLAIM, ExternalEmailVerification.EMAIL_VERIFIED_CLAIM);
                options.ClaimActions.MapJsonKey(GoogleAddressProof.HOSTED_DOMAIN_CLAIM, GoogleAddressProof.HOSTED_DOMAIN_CLAIM);
            });
        }

        if (oidcSettings.Enabled && !string.IsNullOrEmpty(oidcSettings.Issuer) && !string.IsNullOrEmpty(oidcSettings.ClientId))
        {
            _ = authBuilder.AddOpenIdConnect(ExternalLoginProviders.OIDC, oidcSettings.DisplayName, options =>
            {
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.Authority = oidcSettings.Issuer;
                options.ClientId = oidcSettings.ClientId;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.Scope.Add(OpenIdConnectScope.Email);
                options.GetClaimsFromUserInfoEndpoint = true;
                if (!string.IsNullOrEmpty(oidcSettings.ClientSecret))
                {
                    options.ClientSecret = oidcSettings.ClientSecret;
                }
            });
        }

        _ = services.AddIdentityCore<User>(options =>
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(passwordSettings.LockoutDurationMinutes);
            options.Lockout.MaxFailedAccessAttempts = passwordSettings.MaxFailedAccessAttempts;
        })
            .AddRoles<IdentityRole>()
            .AddPlatformUserStores()
            .AddSignInManager<AdminAppSignInManager>()
            .AddDefaultTokenProviders();

        return services;
    }
}
