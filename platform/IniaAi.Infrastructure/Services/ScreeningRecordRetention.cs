using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.Screening;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The retention rule for the screening record: the row is permanent, the evidence excerpt and the
/// judge's reasoning go when the document they were taken from is hard-deleted.
/// <see cref="ScreeningRecord.ExcerptClearedAt"/> is stamped only on a row that had an excerpt, so it
/// keeps telling a removed excerpt from one the judge never cited. Matched on either the screened or the
/// originating id, because a confidential run screens an anonymised derivative of the sent
/// document. Two statements rather than one with OR, so each seeks its own id index; the content
/// predicate makes the summed count a distinct-row count.
/// </summary>
public sealed class ScreeningRecordRetention : IDocumentPurgeStep
{
    private readonly UserDataDbContext _db;

    public ScreeningRecordRetention(UserDataDbContext db)
    {
        _db = db;
    }

    public string Name => "ScreeningRecord.EvidenceExcerpt and Reasoning (nullified)";

    public IReadOnlyList<LooseReferenceDisposition> Dispositions { get; } =
    [
        new(
            typeof(ScreeningRecord),
            [nameof(ScreeningRecord.DocumentId), nameof(ScreeningRecord.OriginatingDocumentId), nameof(ScreeningRecord.WorkflowExecutionId)],
            [nameof(ScreeningRecord.EvidenceExcerpt), nameof(ScreeningRecord.Reasoning)]),
    ];

    public async Task<int> RunAsync(IQueryable<string> purgedDocumentIds, CancellationToken cancellationToken)
    {
        DateTime excerptClearedAt = DateTime.UtcNow;
        int count = await _db.ScreeningRecords
            .IgnoreQueryFilters()
            .Where(r => (r.EvidenceExcerpt != null || r.Reasoning != null)
                && r.DocumentId != null
                && purgedDocumentIds.Contains(r.DocumentId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ExcerptClearedAt, r => r.EvidenceExcerpt != null ? excerptClearedAt : r.ExcerptClearedAt)
                .SetProperty(r => r.EvidenceExcerpt, (string?)null)
                .SetProperty(r => r.Reasoning, (string?)null), cancellationToken);
        count += await _db.ScreeningRecords
            .IgnoreQueryFilters()
            .Where(r => (r.EvidenceExcerpt != null || r.Reasoning != null)
                && r.OriginatingDocumentId != null
                && purgedDocumentIds.Contains(r.OriginatingDocumentId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ExcerptClearedAt, r => r.EvidenceExcerpt != null ? excerptClearedAt : r.ExcerptClearedAt)
                .SetProperty(r => r.EvidenceExcerpt, (string?)null)
                .SetProperty(r => r.Reasoning, (string?)null), cancellationToken);

        return count;
    }
}
