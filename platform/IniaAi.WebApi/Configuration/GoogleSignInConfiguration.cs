using IniaAi.Configuration.Security;

namespace IniaAi.WebApi.Configuration;

/// <summary>The settings of Google sign-in.</summary>
public static class GoogleSignInConfiguration
{
    /// <summary>
    /// Binds the Google sign-in settings, and refuses to start with Google sign-in on and no client
    /// id: every Google token would then fail its audience check.
    /// </summary>
    public static IServiceCollection AddGoogleSignInSettings(this IServiceCollection services, IConfiguration configuration)
    {
        _ = services.AddOptions<GoogleAuthSettings>()
            .Bind(configuration.GetSection(GoogleAuthSettings.SectionName))
            .Validate(GoogleAuthSettings.HasClientIdWhenEnabled, GoogleAuthSettings.CLIENT_ID_REFUSAL)
            .ValidateOnStart();

        return services;
    }
}
