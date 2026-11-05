using System.Linq.Expressions;
using ErrorOr;
using IniaAi.Application.Commands.Counterparties;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Specifications.Common;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.Counterparties.ExportCounterparties;

/// <summary>
/// Packs every active counterparty of the organization into one bundle, in a stable order so
/// two exports of the same data diff cleanly. Types travel by name, and only the types the
/// bundled counterparties use.
/// </summary>
public class ExportCounterpartiesQueryHandler : IRequestHandler<ExportCounterpartiesQuery, ErrorOr<CounterpartyBundleDto>>
{
    private static readonly Expression<Func<Counterparty, CounterpartyForExport>> _toExportRow = ProjectionExpressions.Of<Counterparty, CounterpartyForExport>(counterparty => new CounterpartyForExport(
        CounterpartyFields.ToBundleEntry.Inline(counterparty),
        counterparty.ParentId,
        counterparty.Properties,
        counterparty.CounterpartyTypes.Select(type => new TypeForExport(type.Id, type.Name, type.Description)).ToList()));

    private readonly IUserDataDbContext _context;

    public ExportCounterpartiesQueryHandler(IUserDataDbContext context)
    {
        _context = context;
    }

    public async Task<ErrorOr<CounterpartyBundleDto>> Handle(ExportCounterpartiesQuery request, CancellationToken cancellationToken)
    {
        List<CounterpartyForExport> rows = await _context.Counterparties
            .AsNoTracking()
            .AsSplitQuery()
            .Where(c => c.OrganizationId == request.OrganizationId)
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Select(_toExportRow)
            .ToListAsync(cancellationToken);

        string? organizationName = await _context.Organizations
            .AsNoTracking()
            .Where(o => o.Id == request.OrganizationId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(cancellationToken);

        HashSet<string> exportedIds = new(rows.Select(r => r.Entry.Id), StringComparer.Ordinal);

        return new CounterpartyBundleDto
        {
            SourceInstance = request.InstanceName,
            SourceOrganizationId = request.OrganizationId,
            SourceOrganizationName = organizationName,
            ExportedAt = DateTime.UtcNow,
            CounterpartyTypes = [.. rows
                .SelectMany(r => r.Types)
                .DistinctBy(t => t.Id)
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => new CounterpartyBundleTypeDto { Name = t.Name, Description = t.Description })],
            Counterparties = [.. rows.Select(row => ToEntry(row, exportedIds))],
        };
    }

    private static CounterpartyBundleEntryDto ToEntry(CounterpartyForExport row, HashSet<string> exportedIds)
    {
        CounterpartyBundleEntryDto entry = row.Entry;
        // A parent outside the export set is a soft-deleted one; every screen on the source
        // already shows such a row as a root, so it travels as a root.
        entry.ParentId = row.ParentId is not null && exportedIds.Contains(row.ParentId) ? row.ParentId : null;
        entry.Properties = row.Properties is null ? null : new Dictionary<string, string>(row.Properties, StringComparer.OrdinalIgnoreCase);
        entry.CounterpartyTypeNames = [.. row.Types.Select(type => type.Name).Order(StringComparer.Ordinal)];
        return entry;
    }

    private sealed record CounterpartyForExport(
        CounterpartyBundleEntryDto Entry,
        string? ParentId,
        Dictionary<string, string>? Properties,
        List<TypeForExport> Types);

    private sealed record TypeForExport(string Id, string Name, string? Description);
}
