using System.Security.Claims;

namespace IniaAi.Services.Abstractions.Services;

/// <summary>
/// Provides access to the current authenticated user's or client's identity from the HTTP context.
/// Supports both user-based and client credentials authentication flows.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Gets the user ID from the 'sub' claim (null in client credential flows).
    /// </summary>
    string? GetUserId();

    /// <summary>
    /// Gets the organization ID from the custom 'organization_id' claim.
    /// </summary>
    string? GetOrganizationId();

    /// <summary>
    /// Gets the list of organization IDs that the current user has access to.
    /// </summary>
    IEnumerable<string> GetOrganizationIds();

    /// <summary>
    /// Gets the current tenant ID from the multi-tenant context (from header validation).
    /// </summary>
    string? GetCurrentTenantId();

    /// <summary>
    /// Gets the client ID from the 'client_id' claim (present in client credentials flow).
    /// </summary>
    string? GetClientId();

    /// <summary>
    /// Gets the user's email address (if authenticated as a user).
    /// </summary>
    string? GetEmail();

    /// <summary>
    /// Gets the user's username(if authenticated as a user).
    /// </summary>
    string? GetUsername();

    /// <summary>
    /// True if the request is authenticated as a user (password or authorization code flow).
    /// </summary>
    bool IsUserFlow();

    /// <summary>
    /// True if the request is authenticated as a client via client credentials flow.
    /// </summary>
    bool IsClientCredentialsFlow();

    /// <summary>
    /// Returns the raw ClaimsPrincipal for advanced usage.
    /// </summary>
    ClaimsPrincipal? GetPrincipal();
}
