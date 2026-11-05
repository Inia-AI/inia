using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Clears the stored JSON of old <c>WorkflowVersion</c> rows to reclaim storage. It only ever NULLs
/// the payload (and stamps <c>PayloadTrimmedAt</c>); it never deletes a row, so the content hash and
/// lineage every comparison and provenance path reads survive untouched. Which payloads to clear is
/// decided by <see cref="WorkflowVersionRetentionPlanner"/> from scalar metadata, so no JSON is ever
/// loaded to make the decision.
/// </summary>
public class WorkflowVersionRetentionService : IWorkflowVersionRetentionService
{
    private const int BATCH_SIZE = 100;

    private readonly CoreDbContext _db;
    private readonly ILogger<WorkflowVersionRetentionService> _logger;

    public WorkflowVersionRetentionService(
        CoreDbContext db,
        ILogger<WorkflowVersionRetentionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PayloadTrimResult> TrimAsync(
        WorkflowVersionRetentionPolicy policy,
        CancellationToken ct = default)
    {
        var errors = new List<string>();

        List<WorkflowVersionMetadata> metadata = await _db.WorkflowVersions
            .Select(version => new WorkflowVersionMetadata(
                version.Id,
                version.WorkflowTypeId,
                version.CreatedAt,
                version.ContentHash,
                version.PayloadTrimmedAt,
                version.WorkflowJsonCompressed != null))
            .ToListAsync(ct);

        IReadOnlyList<string> trimIds = WorkflowVersionRetentionPlanner.Plan(metadata, policy, DateTime.UtcNow);

        long bytesEstimate = await EstimateBytesAsync(trimIds, ct);

        if (policy.ReportOnly)
        {
            _logger.LogInformation(
                "WorkflowVersionRetention: REPORT ONLY: would clear {Count} payloads (~{Bytes} bytes)",
                trimIds.Count, bytesEstimate);

            return new PayloadTrimResult(trimIds.Count, bytesEstimate, WasReportOnly: true, errors);
        }

        DateTime trimmedAt = DateTime.UtcNow;
        int trimmed = 0;

        for (int start = 0; start < trimIds.Count; start += BATCH_SIZE)
        {
            ct.ThrowIfCancellationRequested();

            string[] batch = [.. trimIds.Skip(start).Take(BATCH_SIZE)];

            try
            {
                trimmed += await _db.WorkflowVersions
                    .Where(version => batch.Contains(version.Id))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(version => version.WorkflowJsonCompressed, (byte[]?)null)
                        .SetProperty(version => version.PayloadTrimmedAt, trimmedAt), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WorkflowVersionRetention: batch failed, continuing");
                errors.Add(ex.Message);
            }
        }

        if (trimmed > 0)
        {
            _logger.LogInformation(
                "WorkflowVersionRetention: cleared {Count} payloads (~{Bytes} bytes reclaimed)",
                trimmed, bytesEstimate);
        }

        return new PayloadTrimResult(trimmed, bytesEstimate, WasReportOnly: false, errors);
    }

    private async Task<long> EstimateBytesAsync(IReadOnlyList<string> trimIds, CancellationToken ct)
    {
        if (trimIds.Count == 0)
        {
            return 0;
        }

        try
        {
            return await _db.WorkflowVersions
                .Where(version => trimIds.Contains(version.Id) && version.WorkflowJsonCompressed != null)
                .SumAsync(version => (long)version.WorkflowJsonCompressed!.Length, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WorkflowVersionRetention: byte estimate unavailable");
            return 0;
        }
    }
}
