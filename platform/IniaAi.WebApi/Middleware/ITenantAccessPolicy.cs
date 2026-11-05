namespace IniaAi.WebApi.Middleware;

/// <summary>
/// Decides whether a signed-in user may act in the organization a request names. The platform's
/// rule is <see cref="MembershipTenantAccessPolicy"/>; a closed module may replace it.
/// </summary>
public interface ITenantAccessPolicy
{
    /// <summary>Allows the request, or refuses it with the problem the caller gets.</summary>
    ValueTask<TenantAccessDecision> DecideAsync(TenantAccessRequest request, CancellationToken cancellationToken);
}

/// <param name="HttpContext">The request, for a policy that also weighs its method or endpoint.</param>
/// <param name="UserId">The signed-in user.</param>
/// <param name="OrganizationId">The organization the request names.</param>
/// <param name="MemberOrganizationIds">The organizations the user's token lists.</param>
public sealed record TenantAccessRequest(
    HttpContext HttpContext,
    string UserId,
    string OrganizationId,
    IReadOnlySet<string> MemberOrganizationIds);

/// <param name="IsAllowed">Whether the request goes on.</param>
/// <param name="Title">The title of the refusal.</param>
/// <param name="Detail">The text of the refusal.</param>
public sealed record TenantAccessDecision(bool IsAllowed, string? Title = null, string? Detail = null)
{
    /// <summary>The request goes on.</summary>
    public static TenantAccessDecision Allow { get; } = new(true);

    /// <summary>The request is refused with 403 and this problem.</summary>
    public static TenantAccessDecision Refuse(string title, string detail)
    {
        return new TenantAccessDecision(false, title, detail);
    }
}
