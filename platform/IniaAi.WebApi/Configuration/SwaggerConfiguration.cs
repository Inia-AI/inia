using IniaAi.WebApi.Filters;
using IniaAi.WebApi.Swagger;
using Microsoft.OpenApi;
using System.Reflection;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Provides extension methods for configuring Swagger documentation and OAuth2 security.
/// </summary>
public static class SwaggerConfiguration
{
    /// <summary>
    /// Adds and configures Swagger generation, including XML comments, OAuth2 support, and custom filters.
    /// </summary>
    /// <param name="services">The service collection to register Swagger services to.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSwaggerDocs(this IServiceCollection services, IConfiguration configuration)
    {
        _ = services.AddSwaggerGen(options =>
        {
            string xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";

            string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

            options.IncludeXmlComments(xmlPath);

            string contractsXmlFilename = Path.Combine(AppContext.BaseDirectory, $"IniaAi.WebApi.Contracts.xml");
            if (File.Exists(contractsXmlFilename))
            {
                options.IncludeXmlComments(contractsXmlFilename);
            }

            // JWT Bearer scheme
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "Enter JWT token: Bearer {token}",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            // OAuth2 Password Flow scheme
            string tokenUrl = configuration["Swagger:TokenUrl"]!;
            options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    Password = new OpenApiOAuthFlow
                    {
                        TokenUrl = new Uri(tokenUrl),
                        Scopes = new Dictionary<string, string>
                        {
                            { "api", "Access the API" },
                            { "offline_access", "Access refresh tokens" }
                        }
                    }
                }
            });

            // Filters
            options.OperationFilter<FileUploadOperationFilter>();
            options.OperationFilter<CamelCaseQueryParameterFilter>();
            options.OperationFilter<ODataQueryOptionsOperationFilter>();
            options.OperationFilter<TokenRequestMultiExampleFilter>();

            // Register the filter to apply security only to [Authorize] endpoints
            options.OperationFilter<AuthorizeCheckOperationFilter>();

            // Register the filter to add X-Tenant-Id header for multi-tenant operations
            options.OperationFilter<TenantHeaderOperationFilter>();

            options.OperationFilter<SwaggerExamplesOperationFilter>();
        });

        _ = services.ConfigureOptions<ConfigureSwaggerOptions>();

        return services;
    }
}
