using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.WebApi.Middleware;

/// <summary>
/// A user acts in an organization the token lists as a membership; a super admin acts in any.
/// </summary>
public sealed class MembershipTenantAccessPolicy : ITenantAccessPolicy
{
    /// <summary>The title of the refusal.</summary>
    public const string TITLE = "Invalid tenant context";

    /// <summary>The text of the refusal.</summary>
    public const string DETAIL = "You do not have access to this tenant.";

    /// <inheritdoc />
    public ValueTask<TenantAccessDecision> DecideAsync(TenantAccessRequest request, CancellationToken cancellationToken)
    {
        bool allowed = request.HttpContext.User.IsInRole(Roles.SUPER_ADMIN)
            || request.MemberOrganizationIds.Contains(request.OrganizationId);

        return ValueTask.FromResult(allowed ? TenantAccessDecision.Allow : TenantAccessDecision.Refuse(TITLE, DETAIL));
    }
}
