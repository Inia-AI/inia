using Asp.Versioning;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Provides extension methods to configure API versioning.
/// </summary>
public static class ApiVersioningConfiguration
{
    /// <summary>
    /// Adds and configures API versioning and versioned API explorer support.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddConfiguredApiVersioning(this IServiceCollection services)
    {
        _ = services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

        return services;
    }
}
