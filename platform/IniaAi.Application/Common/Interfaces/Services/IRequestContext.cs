namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// Provides access to the current HTTP request's organization and user context.
/// This is populated by middleware and provides a clean way to access tenant and user information.
/// </summary>
public interface IRequestContext
{
    /// <summary>
    /// Gets the organization ID (tenant ID) for the current request.
    /// </summary>
    string? OrganizationId { get; }

    /// <summary>
    /// Gets the user ID for the current request.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Gets a value indicating whether the current request is authenticated (has a user ID).
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets a value indicating whether the current request has tenant context (has an organization ID).
    /// </summary>
    bool HasTenantContext { get; }
}
