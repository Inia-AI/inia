using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Common.Workflows;

public sealed class EveryWorkflowAvailable : IWorkflowAvailability
{
    public Task<bool> CanStartRunAsync(string organizationId, WorkflowConfiguration? configuration, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }
}
