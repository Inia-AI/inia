using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.Screening;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The retention rule for a backtest document row: the row and its verdict are permanent, the
/// judge's reasoning and the excerpt it cited go when the document is hard-deleted.
/// <see cref="ScreeningBacktestDocument.ContentClearedAt"/> says they were taken away. A backtest
/// run row holds no document content and is kept whole.
/// </summary>
public sealed class ScreeningBacktestRetention : IDocumentPurgeStep
{
    private readonly UserDataDbContext _db;

    public ScreeningBacktestRetention(UserDataDbContext db)
    {
        _db = db;
    }

    public string Name => "ScreeningBacktestDocument.Reasoning and EvidenceExcerpt (nullified)";

    public IReadOnlyList<LooseReferenceDisposition> Dispositions { get; } =
    [
        new(
            typeof(ScreeningBacktestDocument),
            [nameof(ScreeningBacktestDocument.DocumentId), nameof(ScreeningBacktestDocument.WorkflowExecutionId)],
            [nameof(ScreeningBacktestDocument.Reasoning), nameof(ScreeningBacktestDocument.EvidenceExcerpt)]),
        new(typeof(ScreeningBacktestRun), [nameof(ScreeningBacktestRun.WorkflowExecutionId)], []),
    ];

    public Task<int> RunAsync(IQueryable<string> purgedDocumentIds, CancellationToken cancellationToken)
    {
        DateTime contentClearedAt = DateTime.UtcNow;

        return _db.ScreeningBacktestDocuments
            .IgnoreQueryFilters()
            .Where(row => (row.Reasoning != null || row.EvidenceExcerpt != null) && purgedDocumentIds.Contains(row.DocumentId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(row => row.ContentClearedAt, contentClearedAt)
                .SetProperty(row => row.Reasoning, (string?)null)
                .SetProperty(row => row.EvidenceExcerpt, (string?)null), cancellationToken);
    }
}
