using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Common.RunLimits;

/// <summary>
/// The pages a run of a workflow type may read, as the platform counts them at upload, which the workflow type list
/// shows as its <c>maxPages</c>. The platform sets no limit, so no type has one; a closed module may read one from the
/// type's configuration.
/// </summary>
public interface IWorkflowPageLimit
{
    /// <summary>The page limit of a run of the type with this configuration; null when a run has none.</summary>
    int? Of(WorkflowConfiguration? configuration);
}
