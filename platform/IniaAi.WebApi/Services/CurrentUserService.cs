using Finbuckle.MultiTenant.Abstractions;
using IniaAi.Common.Constants;
using IniaAi.Infrastructure.MultiTenant;
using IniaAi.Services.Abstractions.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace IniaAi.WebApi.Services;

/// <summary>
/// Provides access to the current authenticated user or client identity using claims in the HTTP context.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMultiTenantContextAccessor<OrganizationTenantInfo> _multiTenantContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="CurrentUserService"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor to access the current request context.</param>
    /// <param name="multiTenantContextAccessor">The multi-tenant context accessor.</param>
    public CurrentUserService(
        IHttpContextAccessor httpContextAccessor,
        IMultiTenantContextAccessor<OrganizationTenantInfo> multiTenantContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        _multiTenantContextAccessor = multiTenantContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    /// <inheritdoc/>
    public string? GetUserId()
    {
        return Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
    }

    /// <inheritdoc/>
    public string? GetOrganizationId()
    {
        // Try tenant_id first (preferred), then fall back to organization_id (legacy)
        return Principal?.FindFirst(TokenClaimNames.TenantId)?.Value
            ?? Principal?.FindFirst(TokenClaimNames.OrganizationId)?.Value;
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetOrganizationIds()
    {
        Claim? orgsClaim = Principal?.FindFirst(TokenClaimNames.TenantIds);
        if (orgsClaim is not null)
        {
            try
            {
                string[]? orgIds = JsonSerializer.Deserialize<string[]>(orgsClaim.Value);
                return orgIds ?? Enumerable.Empty<string>();
            }
            catch (JsonException)
            {
                // Log error if needed, but don't throw
            }
        }

        // Fallback to single organization ID
        string? singleOrgId = GetOrganizationId();
        return !string.IsNullOrEmpty(singleOrgId)
            ? new[] { singleOrgId }
            : Enumerable.Empty<string>();
    }

    /// <inheritdoc/>
    public string? GetCurrentTenantId()
    {
        IMultiTenantContext<OrganizationTenantInfo> tenantContext = _multiTenantContextAccessor.MultiTenantContext;
        return tenantContext?.TenantInfo?.Id;
    }

    /// <inheritdoc/>
    public string? GetClientId()
    {
        return Principal?.FindFirst("client_id")?.Value;
    }

    /// <inheritdoc/>
    public string? GetEmail()
    {
        return Principal?.FindFirst(ClaimTypes.Email)?.Value
        ?? Principal?.FindFirst("email")?.Value;
    }

    /// <inheritdoc/>
    public string? GetUsername()
    {
        return Principal?.FindFirst(ClaimTypes.Name)?.Value
        ?? Principal?.FindFirst("name")?.Value;
    }

    /// <inheritdoc/>
    public bool IsUserFlow()
    {
        return GetUserId() is not null;
    }

    /// <inheritdoc/>
    public bool IsClientCredentialsFlow()
    {
        return GetUserId() is null && GetClientId() is not null;
    }

    /// <inheritdoc/>
    public ClaimsPrincipal? GetPrincipal()
    {
        return Principal;
    }
}
