using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.Signatures.GetSignatureFieldOverrideCounts;

public class GetSignatureFieldOverrideCountsQueryHandler
    : IRequestHandler<GetSignatureFieldOverrideCountsQuery, ErrorOr<IReadOnlyDictionary<string, SignatureFieldOverrideCounts>>>
{
    private readonly ICoreDbContext _coreDbContext;

    public GetSignatureFieldOverrideCountsQueryHandler(ICoreDbContext coreDbContext)
    {
        _coreDbContext = coreDbContext;
    }

    public async Task<ErrorOr<IReadOnlyDictionary<string, SignatureFieldOverrideCounts>>> Handle(
        GetSignatureFieldOverrideCountsQuery request,
        CancellationToken cancellationToken)
    {
        var active = await _coreDbContext.SignatureFieldOverrides
            .AsNoTracking()
            .Where(o => o.WorkflowTypeId == request.WorkflowTypeId)
            .GroupBy(o => o.PlaceholderName)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var pending = await _coreDbContext.PendingSignatureOverrides
            .AsNoTracking()
            .Where(p => p.WorkflowTypeId == request.WorkflowTypeId)
            .GroupBy(p => p.PlaceholderName)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var counts = new Dictionary<string, SignatureFieldOverrideCounts>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in active)
        {
            counts[row.Name] = new SignatureFieldOverrideCounts(row.Count, 0);
        }

        foreach (var row in pending)
        {
            counts[row.Name] = counts.TryGetValue(row.Name, out SignatureFieldOverrideCounts? existing)
                ? existing with { PendingOverrides = row.Count }
                : new SignatureFieldOverrideCounts(0, row.Count);
        }

        return counts;
    }
}
