using IniaAi.Configuration.Extensions;
using IniaAi.WebApi.Filters;
using Microsoft.Net.Http.Headers;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Provides extension methods to configure Cross-Origin Resource Sharing (CORS).
/// </summary>
public static class CorsConfiguration
{
    /// <summary>The name of the API's one CORS policy.</summary>
    public const string POLICY_NAME = "CorsPolicy";
    /// <summary>The setting that lists the origins allowed to call the API from a browser.</summary>
    public const string ALLOWED_ORIGINS_KEY = "Cors:AllowedOrigins";

    private const string ANY_ORIGIN = "*";

    private static readonly string[] _exposedHeaders =
    [
        HeaderNames.ContentDisposition,
        RateLimitHeaders.LIMIT_HEADER,
        RateLimitHeaders.POLICY_HEADER,
        HeaderNames.RetryAfter,
    ];

    /// <summary>
    /// Adds a CORS policy that admits only the origins listed in <c>Cors:AllowedOrigins</c>. An empty
    /// list admits no cross-origin call. A <c>*</c> entry stops startup: the policy allows
    /// credentials, which ASP.NET Core refuses to pair with any origin.
    /// </summary>
    /// <param name="services">The service collection to add CORS to.</param>
    /// <param name="configuration">The configuration that holds the origin list.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddCorsWithPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        string[] origins = configuration.GetList(ALLOWED_ORIGINS_KEY);

        if (origins.Contains(ANY_ORIGIN))
        {
            throw new InvalidOperationException(
                $"{ALLOWED_ORIGINS_KEY} lists '{ANY_ORIGIN}'. List each origin that calls the API from a browser, or none.");
        }

        _ = services.AddCors(options =>
        {
            options.AddPolicy(POLICY_NAME, policy =>
            {
                _ = policy.WithOrigins(origins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    // AllowAnyHeader covers request headers only; response headers stay hidden
                    // from cross-origin JavaScript unless exposed. The download endpoints carry
                    // their filename in Content-Disposition, and the daily allowance its limit.
                    .WithExposedHeaders(_exposedHeaders)
                    .AllowCredentials()
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            });
        });

        return services;
    }
}
