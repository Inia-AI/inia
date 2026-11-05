using Finbuckle.MultiTenant.Abstractions;
using IniaAi.Infrastructure.MultiTenant;
using IniaAi.WebApi.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Middleware;

/// <summary>
/// Middleware that rejects requests to protected endpoints with missing/invalid tenant context
/// BEFORE expensive JWT validation. This runs after UseMultiTenant() but before UseAuthentication().
/// </summary>
public class EarlyTenantRejectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<EarlyTenantRejectionMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EarlyTenantRejectionMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware delegate.</param>
    /// <param name="logger">The logger.</param>
    public EarlyTenantRejectionMiddleware(
        RequestDelegate next,
        ILogger<EarlyTenantRejectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware to check tenant context before authentication.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="tenantAccessor">The multi-tenant context accessor.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        IMultiTenantContextAccessor<OrganizationTenantInfo> tenantAccessor)
    {
        string path = context.Request.Path.Value ?? "";

        // Skip infrastructure endpoints (health checks, swagger, etc.)
        if (MiddlewareConstants.ShouldSkipPath(path))
        {
            await _next(context);
            return;
        }

        // Get endpoint metadata (requires UseRouting() to have been called first)
        Endpoint? endpoint = context.GetEndpoint();

        // Skip if endpoint has [AllowAnonymous] (public endpoints like /meta, /connect/token)
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await _next(context);
            return;
        }

        // Skip if endpoint has [TenantOptional] (authenticated but tenant-agnostic endpoints)
        if (endpoint?.Metadata.GetMetadata<TenantOptionalAttribute>() is not null)
        {
            await _next(context);
            return;
        }

        // Check if endpoint requires authorization
        bool requiresAuth = endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null;

        // Check Finbuckle's result - it already validated tenant exists and is active
        OrganizationTenantInfo? tenantInfo = tenantAccessor.MultiTenantContext?.TenantInfo;

        // If endpoint requires auth but no valid tenant was resolved, reject BEFORE expensive JWT validation
        if (requiresAuth && tenantInfo is null)
        {
            _logger.LogWarning(
                "Early tenant rejection: Request to protected endpoint {Path} rejected - " +
                "missing or invalid X-Tenant-Id header. This prevents expensive JWT validation.",
                path);

            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Tenant context required",
                Detail = "A valid X-Tenant-Id header is required to access this endpoint.",
                Instance = path
            };

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problemDetails);
            return;
        }

        // Tenant is valid or endpoint doesn't require auth, continue to UseAuthentication()
        await _next(context);
    }
}
