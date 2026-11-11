using System.Security.Claims;

namespace IniaAi.Domain.Entities.UserManagement;

public class Roles
{
    public const string SUPER_ADMIN = "SuperAdmin";
    public const string ADMIN = "Admin";
    public const string USER = "User";
    public const string WORKFLOW_EDITOR = "WorkflowEditor";

    /// <summary>
    /// Every surface that admits a workflow editor also admits a super admin. ASP.NET role
    /// checks have no hierarchy, so the pair must be spelled out — gate editor surfaces with
    /// this constant, never with <see cref="WORKFLOW_EDITOR"/> alone.
    /// </summary>
    public const string EDITOR_ROLES = $"{WORKFLOW_EDITOR},{SUPER_ADMIN}";

    /// <summary>
    /// Every administrative surface admits an admin and a super admin. ASP.NET role checks have
    /// no hierarchy, so the pair must be spelled out — gate those surfaces with this constant,
    /// never with <see cref="ADMIN"/> alone.
    /// </summary>
    public const string ADMIN_ROLES = $"{ADMIN},{SUPER_ADMIN}";

    /// <summary>
    /// Surfaces that change what every workflow of an organization uses, such as its report
    /// templates, admit the editor roles and an admin. ASP.NET role checks have no hierarchy, so
    /// gate those surfaces with this constant, never with a bare role constant.
    /// </summary>
    public const string EDITOR_AND_ADMIN_ROLES = $"{EDITOR_ROLES},{ADMIN}";

    /// <summary>
    /// A signature contributor can view the Signatures page, propose overrides for review,
    /// and vote on others' proposals. Signature-review surfaces are gated to SuperAdmin or
    /// SignatureContributor — Admin alone cannot reach them.
    /// </summary>
    public const string SIGNATURE_CONTRIBUTOR = "SignatureContributor";

    /// <summary>
    /// An operator runs the deployment day to day: the server log, executed-workflow downloads,
    /// screening records, model pricing and settings, token usage and data clean-up. Only a super
    /// admin grants it, because those surfaces read across organizations.
    /// </summary>
    public const string OPERATOR = "Operator";

    /// <summary>
    /// Every operations surface admits an operator and a super admin. ASP.NET role checks have no
    /// hierarchy, so the pair must be spelled out — gate those surfaces with this constant, never
    /// with <see cref="OPERATOR"/> alone.
    /// </summary>
    public const string OPERATOR_ROLES = $"{OPERATOR},{SUPER_ADMIN}";

    /// <summary>
    /// Surfaces an admin uses for their own organization and an operator uses to oversee and clean
    /// up the deployment: document deletion and the token-usage dashboard.
    /// </summary>
    public const string ADMIN_AND_OPERATOR_ROLES = $"{ADMIN_ROLES},{OPERATOR}";

    /// <summary>
    /// Whether <paramref name="user"/> holds any of <paramref name="roles"/>, a comma-separated list
    /// such as <see cref="EDITOR_ROLES"/>: what [Authorize(Roles = roles)] admits.
    /// </summary>
    public static bool HeldBy(ClaimsPrincipal user, string roles)
    {
        return roles.Split(',').Any(user.IsInRole);
    }
}
