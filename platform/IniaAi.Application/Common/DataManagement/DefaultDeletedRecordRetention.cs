namespace IniaAi.Application.Common.DataManagement;

public sealed class DefaultDeletedRecordRetention : IDeletedRecordRetention
{
    public Task<int?> RetentionDaysAsync(string organizationId, CancellationToken cancellationToken)
    {
        return Task.FromResult<int?>(null);
    }
}
