namespace IniaAi.Application.Common.Workflows;

public sealed class AccessRowsDecideReach : IWorkflowTypeReach
{
    public Task<IReadOnlyCollection<string>?> TypeIdsAsync(string organizationId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyCollection<string>?>(null);
    }

    public Task<bool> IsReachedAsync(string workflowTypeId, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}
