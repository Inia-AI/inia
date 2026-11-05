namespace IniaAi.WebApi.Middleware;

/// <summary>
/// Shared constants used across middleware components.
/// </summary>
public static class MiddlewareConstants
{
    /// <summary>
    /// Paths that should bypass tenant validation and authentication middleware.
    /// These are infrastructure endpoints that don't require authentication or tenant context.
    /// </summary>
    public static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        // Health check endpoints
        "/health",
        "/healthz",
        "/ready",
        "/readyz",
        "/readiness",
        "/live",
        "/livez",

        // Swagger/OpenAPI documentation
        "/swagger",

        // NOTE: /api/v1/auth is NOT excluded here because:
        // - /auth/providers has [AllowAnonymous] which bypasses tenant checks anyway
        // - /auth/2fa/* endpoints require authentication AND tenant context
        // The [AllowAnonymous] attribute handles exclusion for public endpoints.
    };

    /// <summary>
    /// Checks if a given path should be excluded from middleware processing.
    /// </summary>
    /// <param name="path">The request path to check.</param>
    /// <returns>True if the path should be excluded, false otherwise.</returns>
    public static bool ShouldSkipPath(string path)
    {
        return ExcludedPaths.Any(excluded =>
            path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase));
    }
}
