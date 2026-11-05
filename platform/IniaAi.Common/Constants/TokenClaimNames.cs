namespace IniaAi.Common.Constants;

public static class TokenClaimNames
{
    /// <summary>
    /// Legacy claim name for organization/tenant ID. Use TenantId for new code.
    /// </summary>
    public const string OrganizationId = "organization_id";

    /// <summary>
    /// Preferred claim name for tenant ID. Duplicates OrganizationId for backwards compatibility.
    /// </summary>
    public const string TenantId = "tenant_id";

    /// <summary>
    /// JSON array of every organization the caller may act in; the tenant check admits a request
    /// for any of them.
    /// </summary>
    public const string TenantIds = "tenant_ids";

    /// <summary>
    /// Claim carried by per-run agent runtime tokens, binding the token to a single workflow execution.
    /// </summary>
    public const string ExecutionId = "execution_id";
}
