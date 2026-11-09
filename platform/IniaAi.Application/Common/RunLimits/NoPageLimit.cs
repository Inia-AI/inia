using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Common.RunLimits;

public sealed class NoPageLimit : IWorkflowPageLimit
{
    public int? Of(WorkflowConfiguration? configuration)
    {
        return null;
    }
}
