using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// Whether an organization that can reach a workflow type can start a new run of it now. On the platform every type
/// an organization can reach can start; a closed module may hold one back.
/// </summary>
public interface IWorkflowAvailability
{
    /// <summary>Whether the organization can start a run of the type with this configuration.</summary>
    Task<bool> CanStartRunAsync(string organizationId, WorkflowConfiguration? configuration, CancellationToken cancellationToken);
}
