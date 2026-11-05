using IniaAi.Application.Common.Interfaces.Services;

namespace IniaAi.WebApi.Services;

/// <summary>
/// Implementation of <see cref="IRequestContext"/> that holds the current request's organization and user context.
/// This is populated by <see cref="Middleware.TenantUserContextMiddleware"/> for each HTTP request.
/// </summary>
public class RequestContext : IRequestContext
{
    /// <inheritdoc />
    public string? OrganizationId { get; set; }

    /// <inheritdoc />
    public string? UserId { get; set; }

    /// <inheritdoc />
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);

    /// <inheritdoc />
    public bool HasTenantContext => !string.IsNullOrEmpty(OrganizationId);
}
