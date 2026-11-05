using IniaAi.Domain.Common;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// Junction table managing many-to-many access control between WorkflowTypes and Organizations.
/// Determines which tenants can see and use specific workflow types.
/// Lives in Core DB with string reference to UserData.Organization.
/// </summary>
public class WorkflowTypeOrganizationAccess : Entity<WorkflowTypeOrganizationAccess>
{
    /// <summary>
    /// Gets or sets the workflow type identifier.
    /// </summary>
    public string WorkflowTypeId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the organization identifier.
    /// String reference to UserData.Organization.Id (cross-database reference).
    /// </summary>
    public string OrganizationId { get; set; } = default!;

    /// <summary>
    /// Navigation property to the workflow type.
    /// </summary>
    public WorkflowType WorkflowType { get; set; } = default!;
}
