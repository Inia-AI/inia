using IniaAi.Configuration;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Configuration.Messaging;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Security;

using IniaAi.WebApi.Configuration;

namespace IniaAi.WebApi.Extensions;

/// <summary>
/// Provides extension methods for configuring platform settings in the IniaAi Web API.
/// </summary>
public static class IConfigurationExtensions
{
    /// <summary>
    /// Configures the platform settings by binding the "PlatformSettings" section of the configuration
    /// to the <see cref="PlatformSettings"/> class and registers related settings classes with the service collection.
    /// </summary>
    /// <param name="configuration">The application's configuration.</param>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The bound <see cref="PlatformSettings"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when the "PlatformSettings" section is not found in the configuration.</exception>
    public static PlatformSettings ConfigurePlatformSettings(this IConfiguration configuration, WebApplicationBuilder builder)
    {
        PlatformSettings platformSettings = builder.Configuration.GetSection("PlatformSettings").Get<PlatformSettings>()
            ?? throw new ArgumentException("PlatformSettings not found in configuration.");

        _ = builder.Services.Configure<PlatformSettings>(configuration.GetSection("PlatformSettings"))
            .Configure<IniaAiSettings>(configuration.GetSection("PlatformSettings:IniaAi"))
            .Configure<AnthropicSettings>(configuration.GetSection("PlatformSettings:Anthropic"))
            .Configure<CohereSettings>(configuration.GetSection("PlatformSettings:Cohere"))
            .Configure<MessageProcessorSettings>(configuration.GetSection("PlatformSettings:MessageProcessor"))
            .Configure<ExchangeRateApiSettings>(configuration.GetSection("PlatformSettings:ExchangeRateApi"))
            .Configure<TwoFactorSettings>(configuration.GetSection(TwoFactorSettings.SectionName))
            .AddGoogleSignInSettings(configuration)
            .Configure<OidcAuthSettings>(configuration.GetSection(OidcAuthSettings.SectionName))
            .Configure<PasswordAuthSettings>(configuration.GetSection(PasswordAuthSettings.SectionName))
            .Configure<WorkflowVersionRetentionSettings>(configuration.GetSection(WorkflowVersionRetentionSettings.SECTION_NAME))
            .Configure<WorkflowExecutionJanitorSettings>(configuration.GetSection(WorkflowExecutionJanitorSettings.SECTION_NAME))
            .Configure<TokenUsageTotalsSettings>(configuration.GetSection(TokenUsageTotalsSettings.SECTION_NAME))
            .Configure<AgentRunSessionBundleReaperSettings>(configuration.GetSection(AgentRunSessionBundleReaperSettings.SECTION_NAME))
            .Configure<WebApiCallSettings>(configuration.GetSection(WebApiCallSettings.SECTION_NAME));

        _ = builder.Services.AddOptions<ContentRefinementSettings>()
            .BindConfiguration(ContentRefinementSettings.SECTION_NAME)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services.AddOptions<JwtSettings>()
            .BindConfiguration(JwtSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return platformSettings;
    }
}
