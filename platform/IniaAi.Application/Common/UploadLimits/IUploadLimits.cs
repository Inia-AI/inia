namespace IniaAi.Application.Common.UploadLimits;

/// <summary>
/// Whether an organization may store one more uploaded file. The platform sets no limit; a closed
/// module may set one. A run's own outputs never ask.
/// </summary>
public interface IUploadLimits
{
    /// <summary>
    /// The early check, before the upload's bytes are read: null when the organization may upload
    /// one more file this hour, as far as can be told without the file.
    /// </summary>
    Task<UploadRefusal?> CheckRateAsync(UploadLimitsRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="store"/> when the file fits the organization's limits, and returns why not
    /// otherwise, without running it. An implementation that limits the organization checks and stores
    /// in one transaction under a lock on the organization, so uploads that arrive together cannot
    /// pass a limit between them.
    /// </summary>
    Task<UploadRefusal?> StoreWithinLimitsAsync(
        UploadLimitsRequest request,
        Func<CancellationToken, Task> store,
        CancellationToken cancellationToken);
}
