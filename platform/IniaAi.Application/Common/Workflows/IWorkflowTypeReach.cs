namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// The workflow types an organization reaches where a closed module, not the organization's access rows, decides them.
/// On the platform the access rows decide for every organization.
/// </summary>
public interface IWorkflowTypeReach
{
    /// <summary>The ids of the types the organization reaches, or null where its access rows decide.</summary>
    Task<IReadOnlyCollection<string>?> TypeIdsAsync(string organizationId, CancellationToken cancellationToken);

    /// <summary>Whether an organization whose types this decides reaches the type.</summary>
    Task<bool> IsReachedAsync(string workflowTypeId, CancellationToken cancellationToken);
}
