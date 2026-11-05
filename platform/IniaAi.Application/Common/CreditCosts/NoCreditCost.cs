using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Common.CreditCosts;

public sealed class NoCreditCost : IWorkflowCreditCost
{
    public int? Of(WorkflowConfiguration? configuration)
    {
        return null;
    }
}
