namespace IniaAi.Application.Common.UploadLimits;

public sealed class NoUploadLimits : IUploadLimits
{
    public Task<UploadRefusal?> CheckRateAsync(UploadLimitsRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult<UploadRefusal?>(null);
    }

    public async Task<UploadRefusal?> StoreWithinLimitsAsync(
        UploadLimitsRequest request,
        Func<CancellationToken, Task> store,
        CancellationToken cancellationToken)
    {
        await store(cancellationToken);
        return null;
    }
}
