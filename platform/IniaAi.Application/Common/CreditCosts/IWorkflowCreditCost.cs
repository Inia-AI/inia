using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Common.CreditCosts;

/// <summary>
/// The credits a run of a workflow type costs, which the workflow type list shows as its
/// <c>creditCost</c>. The platform charges no credits, so no type has a cost; a closed module may
/// read one from the type's configuration.
/// </summary>
public interface IWorkflowCreditCost
{
    /// <summary>The cost of a run of the type with this configuration; null when a run has none.</summary>
    int? Of(WorkflowConfiguration? configuration);
}
