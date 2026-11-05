using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace IniaAi.Infrastructure.MultiTenant;

/// <summary>
/// A custom Finbuckle.MultiTenant strategy that resolves tenant from HTTP header and validates against user claims.
/// </summary>
public class HeaderTenantStrategy : IMultiTenantStrategy
{
    private readonly ILogger<HeaderTenantStrategy> _logger;
    private const string TENANT_HEADER = "X-Tenant-Id";

    public HeaderTenantStrategy(ILogger<HeaderTenantStrategy> logger)
    {
        _logger = logger;
    }

    public async Task<string?> GetIdentifierAsync(object context)
    {
        if (context is not HttpContext httpContext)
        {
            _logger.LogWarning("Context is not an HttpContext");
            return null;
        }

        // Try to get tenant ID from header
        if (!httpContext.Request.Headers.TryGetValue(TENANT_HEADER, out StringValues tenantIdHeader))
        {
            _logger.LogDebug("No {HeaderName} header found in request", TENANT_HEADER);
            return null;
        }

        string requestedTenantId = tenantIdHeader.ToString();
        if (string.IsNullOrWhiteSpace(requestedTenantId))
        {
            _logger.LogWarning("Empty tenant ID in header");
            return null;
        }

        // Return the tenant ID from the header
        // OrganizationTenantStore will validate that:
        // 1. The tenant exists and is active
        // 2. The user has access to this tenant (after UseAuthentication() has run)
        _logger.LogDebug("Tenant {TenantId} from header - will be validated by OrganizationTenantStore", requestedTenantId);
        return requestedTenantId;
    }
}
