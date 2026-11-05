using Asp.Versioning.ApiExplorer;
using IniaAi.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Configures Swagger generation options for each available API version.
/// </summary>
/// <remarks>
/// This implementation works in conjunction with <see cref="IApiVersionDescriptionProvider"/>
/// to generate a Swagger document per API version.
/// </remarks>
public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;
    private readonly string? _contactEmail;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureSwaggerOptions"/> class.
    /// </summary>
    /// <param name="provider">
    /// The API version description provider used to enumerate all API versions.
    /// </param>
    /// <param name="platformSettings">The settings that name the support address, if any.</param>
    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider, IOptions<PlatformSettings> platformSettings)
    {
        _provider = provider;
        _contactEmail = platformSettings.Value.ApiDocsContactEmail;
    }

    /// <summary>
    /// Configures Swagger documentation for each API version discovered by the version provider.
    /// </summary>
    /// <param name="options">The Swagger generation options to configure.</param>
    public void Configure(SwaggerGenOptions options)
    {
        foreach (ApiVersionDescription description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, CreateInfoForApiVersion(description, _contactEmail));
        }
    }

    /// <summary>
    /// Creates an <see cref="OpenApiInfo"/> object for the given API version.
    /// </summary>
    /// <param name="description">The API version description.</param>
    /// <param name="contactEmail">The support address, or null for no contact.</param>
    /// <returns>An <see cref="OpenApiInfo"/> object containing metadata for the version.</returns>
    private static OpenApiInfo CreateInfoForApiVersion(ApiVersionDescription description, string? contactEmail)
    {
        var info = new OpenApiInfo
        {
            Title = "IniaAI API",
            Version = description.ApiVersion.ToString(),
            Description = "Core API for IniaAI services.",
            Contact = string.IsNullOrWhiteSpace(contactEmail)
                ? null
                : new OpenApiContact
                {
                    Name = "Support",
                    Email = contactEmail
                },
            License = new OpenApiLicense
            {
                Name = "Apache License 2.0",
                Url = new Uri("https://www.apache.org/licenses/LICENSE-2.0")
            }
        };

        if (description.IsDeprecated)
        {
            info.Description += " **This API version is deprecated.**";
        }

        return info;
    }
}
