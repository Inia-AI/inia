using Finbuckle.MultiTenant.Abstractions;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Constants;
using IniaAi.Infrastructure.MultiTenant;
using IniaAi.Services.Abstractions.Services;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace IniaAi.WebApi.Middleware;

/// <summary>
/// Middleware that populates IRequestContext with organization and user information
/// and validates that authenticated users have proper tenant context.
/// This runs after UseAuthentication() but before UseAuthorization().
/// </summary>
public class TenantUserContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantUserContextMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantUserContextMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware delegate.</param>
    /// <param name="logger">The logger.</param>
    public TenantUserContextMiddleware(
        RequestDelegate next,
        ILogger<TenantUserContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware to populate request context with tenant and user information.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="requestContext">The request context to populate.</param>
    /// <param name="currentUserService">The current user service.</param>
    /// <param name="tenantAccessor">The multi-tenant context accessor.</param>
    /// <param name="tenantAccessPolicy">Decides whether the user may act in the requested organization.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        IRequestContext requestContext,
        ICurrentUserService currentUserService,
        IMultiTenantContextAccessor<OrganizationTenantInfo> tenantAccessor,
        ITenantAccessPolicy tenantAccessPolicy)
    {
        string path = context.Request.Path.Value ?? "";

        // Skip infrastructure endpoints (health checks, swagger, etc.)
        if (MiddlewareConstants.ShouldSkipPath(path))
        {
            await _next(context);
            return;
        }

        // Get endpoint metadata
        Endpoint? endpoint = context.GetEndpoint();

        // Skip if endpoint has [AllowAnonymous] (public endpoints like /meta, /connect/token, /passwords/forgot)
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await _next(context);
            return;
        }

        // Skip if endpoint has [TenantOptional] (authenticated but tenant-agnostic endpoints)
        if (endpoint?.Metadata.GetMetadata<TenantOptionalAttribute>() is not null)
        {
            // Still populate IRequestContext with userId for tenant-optional endpoints
            string? userIdForTenantOptional = currentUserService.GetUserId();
            if (requestContext is RequestContext mutableContextForTenantOptional)
            {
                mutableContextForTenantOptional.UserId = userIdForTenantOptional;
                // organizationId remains null for tenant-optional endpoints
            }

            await _next(context);
            return;
        }

        // Populate IRequestContext with organization and user information
        // This makes organizationId and userId easily accessible to controllers and handlers
        string? organizationId = currentUserService.GetCurrentTenantId();
        string? userId = currentUserService.GetUserId();

        if (requestContext is RequestContext mutableContext)
        {
            mutableContext.OrganizationId = organizationId;
            mutableContext.UserId = userId;
        }

        // Validate organizationId for endpoints that require authorization
        bool requiresAuth = endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null;

        if (requiresAuth && !string.IsNullOrEmpty(userId))
        {
            // User IS authenticated (has userId) but missing organizationId
            // This should not normally happen if Finbuckle is working correctly,
            // but we check as defense-in-depth
            if (string.IsNullOrEmpty(organizationId))
            {
                _logger.LogWarning(
                    "Request to {Path} rejected: Organization context missing for authenticated user {UserId}. " +
                    "This indicates a configuration issue with Finbuckle.MultiTenant or missing tenant claims in JWT.",
                    path, userId);

                var problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Tenant context required",
                    Detail = "User is authenticated but does not belong to any tenant. Please contact support.",
                    Instance = path
                };

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(problemDetails);
                return;
            }

            // Validate user has access to the requested tenant
            // This check happens AFTER UseAuthentication() when JWT claims are available
            OrganizationTenantInfo? tenantInfo = tenantAccessor.MultiTenantContext?.TenantInfo;
            if (tenantInfo is not null) // Tenant was resolved by Finbuckle
            {
                HashSet<string> userOrganizations = GetUserOrganizationsFromClaims(context.User);

                TenantAccessDecision decision = await tenantAccessPolicy.DecideAsync(
                    new TenantAccessRequest(context, userId, organizationId, userOrganizations),
                    context.RequestAborted);

                if (!decision.IsAllowed)
                {
                    _logger.LogWarning(
                        "Request to {Path} rejected: User {UserId} does not have access to organization {OrganizationId}. " +
                        "User's organizations: {UserOrgs}",
                        path, userId, organizationId, string.Join(", ", userOrganizations));

                    var problemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Title = decision.Title,
                        Detail = decision.Detail,
                        Instance = path
                    };

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(problemDetails);
                    return;
                }
            }

            _logger.LogTrace(
                "Request to {Path} validated: User {UserId} in organization {OrganizationId}",
                path, userId, organizationId);
        }

        // Continue to UseAuthorization() which will handle 401 for unauthenticated users
        await _next(context);
    }

    private HashSet<string> GetUserOrganizationsFromClaims(ClaimsPrincipal user)
    {
        var organizations = new HashSet<string>();

        // Try to get multiple organizations from the tenant_ids claim
        Claim? orgsClaim = user.FindFirst(TokenClaimNames.TenantIds);
        if (orgsClaim is not null)
        {
            try
            {
                string[]? orgIds = JsonSerializer.Deserialize<string[]>(orgsClaim.Value);
                if (orgIds is not null)
                {
                    foreach (string orgId in orgIds)
                    {
                        if (!string.IsNullOrWhiteSpace(orgId))
                        {
                            _ = organizations.Add(orgId);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize tenant_ids claim");
            }
        }

        // Also check single tenant/organization claims for backward compatibility
        // Try tenant_id first (preferred), then organization_id (legacy)
        Claim? singleTenantClaim = user.FindFirst("tenant_id");
        if (singleTenantClaim is not null && !string.IsNullOrWhiteSpace(singleTenantClaim.Value))
        {
            _ = organizations.Add(singleTenantClaim.Value);
        }
        else
        {
            Claim? singleOrgClaim = user.FindFirst("organization_id");
            if (singleOrgClaim is not null && !string.IsNullOrWhiteSpace(singleOrgClaim.Value))
            {
                _ = organizations.Add(singleOrgClaim.Value);
            }
        }

        return organizations;
    }
}
