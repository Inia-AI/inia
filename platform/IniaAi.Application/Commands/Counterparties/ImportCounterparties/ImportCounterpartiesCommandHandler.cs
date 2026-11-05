using ErrorOr;
using FluentValidation.Results;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Application.Errors;
using IniaAi.Common.Constants;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Counterparties.ImportCounterparties;

/// <summary>
/// Lands a counterparty bundle in the caller's organization. Rows land parents first. Each row
/// matches by id inside the organization (a soft-deleted match reactivates), then by name among
/// the live rows the organization held before the import, else it is created with its source
/// id. Bundle values win on an update. The import never deletes: a counterparty absent from the
/// bundle stays. A row that cannot land is reported with its reason and never fails the import;
/// everything that can land lands in one commit. A dry run computes the same plan and writes
/// nothing.
/// </summary>
public class ImportCounterpartiesCommandHandler : IRequestHandler<ImportCounterpartiesCommand, ErrorOr<CounterpartyImportPlanDto>>
{
    private static readonly CounterpartyBundleEntryValidator _entryValidator = new();

    private readonly ILogger<ImportCounterpartiesCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userData;
    private readonly IUserDataDbContext _context;
    private readonly IDbExceptionAnalyzer _exceptionAnalyzer;

    public ImportCounterpartiesCommandHandler(
        ILogger<ImportCounterpartiesCommandHandler> logger,
        IUserDataUnitOfWork userData,
        IUserDataDbContext context,
        IDbExceptionAnalyzer exceptionAnalyzer)
    {
        _logger = logger;
        _userData = userData;
        _context = context;
        _exceptionAnalyzer = exceptionAnalyzer;
    }

    public async Task<ErrorOr<CounterpartyImportPlanDto>> Handle(ImportCounterpartiesCommand request, CancellationToken cancellationToken)
    {
        CounterpartyBundleDto bundle = request.Bundle;
        if (bundle.BundleFormatVersion != CounterpartyBundleDto.CURRENT_FORMAT_VERSION)
        {
            return CounterpartyBundleErrorFactory.VersionUnsupported(bundle.BundleFormatVersion, CounterpartyBundleDto.CURRENT_FORMAT_VERSION);
        }

        var rows = new CounterpartyImportRowDto?[bundle.Counterparties.Count];
        var landable = new List<CounterpartyBundleEntryDto>();
        var indexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < bundle.Counterparties.Count; index++)
        {
            CounterpartyBundleEntryDto entry = bundle.Counterparties[index];
            ValidationResult validation = _entryValidator.Validate(entry);
            if (!validation.IsValid)
            {
                rows[index] = Skip(entry, CounterpartyImportSkipReason.Invalid, string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
                continue;
            }

            if (!indexById.TryAdd(entry.Id, index))
            {
                rows[index] = Skip(entry, CounterpartyImportSkipReason.DuplicateIdInBundle, $"An earlier row of the bundle carries the id '{entry.Id}'.");
                continue;
            }

            landable.Add(entry);
        }

        ErrorOr<List<CounterpartyBundleEntryDto>> ordered = CounterpartyBundleOrder.ParentsFirst(landable);
        if (ordered.IsError)
        {
            return ordered.Errors;
        }

        HashSet<string> bundleIds = new(
            bundle.Counterparties.Where(e => !string.IsNullOrEmpty(e.Id)).Select(e => e.Id),
            StringComparer.OrdinalIgnoreCase);
        TargetState target = await LoadTargetAsync(request.OrganizationId, bundleIds, request.DryRun, cancellationToken);
        var types = new TypeCatalog(target.Types, bundle.CounterpartyTypes);
        var landing = new Landing(bundleIds);

        foreach (CounterpartyBundleEntryDto entry in ordered.Value)
        {
            rows[indexById[entry.Id]] = Land(entry, request, target, types, landing);
        }

        if (!request.DryRun)
        {
            foreach (CounterpartyType created in types.Created)
            {
                _userData.Repository<CounterpartyType>().Add(created);
            }

            try
            {
                _ = await _userData.CompleteAsync(cancellationToken);
            }
            catch (Exception ex) when (_exceptionAnalyzer.IsUniqueConstraintViolation(ex) || _exceptionAnalyzer.IsConcurrencyConflict(ex))
            {
                _logger.LogWarning(ex, "Counterparty bundle import into {OrganizationId} collided with a concurrent change", request.OrganizationId);
                return CounterpartyBundleErrorFactory.ConcurrentChange();
            }
        }

        CounterpartyImportPlanDto plan = BuildPlan(rows, types, applied: !request.DryRun);
        _logger.LogInformation(
            "Counterparty bundle import into {OrganizationId} (dry run: {DryRun}): {Create} created, {UpdateById} updated by id, {Resurrect} resurrected, {UpdateByName} updated by name, {Unchanged} unchanged, {Skip} skipped, {Types} types created",
            request.OrganizationId, request.DryRun, plan.CreateCount, plan.UpdateByIdCount, plan.ResurrectCount, plan.UpdateByNameCount, plan.UnchangedCount, plan.SkipCount, plan.CounterpartyTypesToCreate.Count);

        return plan;
    }

    private CounterpartyImportRowDto Land(
        CounterpartyBundleEntryDto entry,
        ImportCounterpartiesCommand request,
        TargetState target,
        TypeCatalog types,
        Landing landing)
    {
        string? parentId = null;
        if (!string.IsNullOrEmpty(entry.ParentId))
        {
            if (landing.IsBundled(entry.ParentId))
            {
                if (!landing.TryGetTarget(entry.ParentId, out parentId))
                {
                    return Skip(entry, CounterpartyImportSkipReason.ParentNotLanded, $"The parent '{entry.ParentId}' did not land.");
                }
            }
            else
            {
                Counterparty? parentRow = target.FindLiveById(entry.ParentId);
                if (parentRow is null)
                {
                    return Skip(entry, CounterpartyImportSkipReason.ParentNotLanded, $"The parent '{entry.ParentId}' is neither in the bundle nor in this organization.");
                }

                parentId = parentRow.Id;
            }
        }

        Counterparty? match = target.FindById(entry.Id);
        if (match is null && target.ForeignIds.Contains(entry.Id))
        {
            return Skip(entry, CounterpartyImportSkipReason.IdHeldByAnotherOrganization, $"The id '{entry.Id}' belongs to a {CounterpartyTerms.SINGULAR_LOWER} of another organization.");
        }

        List<Counterparty> sameName = target.FindLiveByName(entry.Name);
        CounterpartyImportDisposition disposition;
        if (match is not null)
        {
            disposition = match.IsDeleted ? CounterpartyImportDisposition.Resurrect : CounterpartyImportDisposition.UpdateById;
        }
        else
        {
            if (sameName.Count > 1)
            {
                return Skip(entry, CounterpartyImportSkipReason.AmbiguousName, $"{sameName.Count} live {CounterpartyTerms.PLURAL_LOWER} are named '{entry.Name}'.");
            }

            match = sameName.SingleOrDefault();
            disposition = match is null ? CounterpartyImportDisposition.Create : CounterpartyImportDisposition.UpdateByName;
        }

        IReadOnlyList<string> typeNames = DistinctTypeNames(entry);
        if (match is not null)
        {
            if (landing.IsClaimed(match.Id))
            {
                return Skip(entry, CounterpartyImportSkipReason.NameCollision, $"An earlier row of the bundle already landed on {CounterpartyTerms.SINGULAR_LOWER} '{match.Id}'.");
            }

            Counterparty? collision = sameName.FirstOrDefault(c => !SameId(c.Id, match.Id));
            if (collision is not null)
            {
                return Skip(entry, CounterpartyImportSkipReason.NameCollision, $"Another live {CounterpartyTerms.SINGULAR_LOWER} ('{collision.Id}') is already named '{entry.Name}'.");
            }

            if (disposition is not CounterpartyImportDisposition.Resurrect && IsSame(match, entry, parentId, typeNames))
            {
                disposition = CounterpartyImportDisposition.Unchanged;
            }
        }

        if (landing.IsNameTaken(entry.Name))
        {
            return Skip(entry, CounterpartyImportSkipReason.NameCollision, $"An earlier row of the bundle already carries the name '{entry.Name}'.");
        }

        string targetId = match?.Id ?? entry.Id;
        landing.Record(entry.Id, targetId, entry.Name);

        if (disposition is not CounterpartyImportDisposition.Unchanged)
        {
            List<CounterpartyType> resolvedTypes = types.Resolve(typeNames);
            if (!request.DryRun)
            {
                if (match is null)
                {
                    match = new Counterparty(request.OrganizationId, entry.Name) { Id = entry.Id };
                    _userData.Repository<Counterparty>().Add(match);
                }

                Apply(match, entry, parentId, resolvedTypes);
            }
        }

        return new CounterpartyImportRowDto
        {
            SourceId = entry.Id,
            Name = entry.Name,
            Disposition = disposition,
            TargetId = targetId,
        };
    }

    private static void Apply(Counterparty row, CounterpartyBundleEntryDto entry, string? parentId, List<CounterpartyType> types)
    {
        CounterpartyFields.CopyText(entry, row);
        row.ParentId = parentId;
        row.IsDeleted = false;
        row.DeletedAt = null;
        row.DeletedByUserId = null;

        row.CounterpartyTypes.Clear();
        foreach (CounterpartyType type in types)
        {
            row.CounterpartyTypes.Add(type);
        }

        row.Properties = CounterpartyPropertiesWriter.Clean(entry.Properties);
    }

    private static bool IsSame(Counterparty row, CounterpartyBundleEntryDto entry, string? parentId, IReadOnlyList<string> typeNames)
    {
        return CounterpartyFields.SameText(row, entry)
            && SameId(row.ParentId, parentId)
            && SameBag(row.Properties, CounterpartyPropertiesWriter.Clean(entry.Properties))
            && row.CounterpartyTypes.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(typeNames);
    }

    private static bool SameBag(Dictionary<string, string>? left, Dictionary<string, string>? right)
    {
        Dictionary<string, string> stored = left ?? [];
        Dictionary<string, string> bundled = right ?? [];

        return stored.Count == bundled.Count
            && stored.All(pair => bundled.TryGetValue(pair.Key, out string? other) && string.Equals(pair.Value, other, StringComparison.Ordinal));
    }

    private static bool SameId(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> DistinctTypeNames(CounterpartyBundleEntryDto entry)
    {
        return [.. (entry.CounterpartyTypeNames ?? [])
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static CounterpartyImportRowDto Skip(CounterpartyBundleEntryDto entry, CounterpartyImportSkipReason reason, string detail)
    {
        return new CounterpartyImportRowDto
        {
            SourceId = entry.Id,
            Name = entry.Name,
            Disposition = CounterpartyImportDisposition.Skip,
            SkipReason = reason,
            Detail = detail,
        };
    }

    private static CounterpartyImportPlanDto BuildPlan(CounterpartyImportRowDto?[] rows, TypeCatalog types, bool applied)
    {
        var plan = new CounterpartyImportPlanDto
        {
            Applied = applied,
            CounterpartyTypesToCreate = [.. types.Created.Select(t => t.Name)],
        };

        foreach (CounterpartyImportRowDto? row in rows)
        {
            if (row is null)
            {
                throw new InvalidOperationException("Every bundled row must have a disposition.");
            }

            plan.Rows.Add(row);
            switch (row.Disposition)
            {
                case CounterpartyImportDisposition.Create:
                    plan.CreateCount++;
                    break;
                case CounterpartyImportDisposition.UpdateById:
                    plan.UpdateByIdCount++;
                    break;
                case CounterpartyImportDisposition.Resurrect:
                    plan.ResurrectCount++;
                    break;
                case CounterpartyImportDisposition.UpdateByName:
                    plan.UpdateByNameCount++;
                    break;
                case CounterpartyImportDisposition.Unchanged:
                    plan.UnchangedCount++;
                    break;
                case CounterpartyImportDisposition.Skip:
                    plan.SkipCount++;
                    break;
            }
        }

        return plan;
    }

    private async Task<TargetState> LoadTargetAsync(string organizationId, HashSet<string> bundleIds, bool dryRun, CancellationToken cancellationToken)
    {
        IQueryable<Counterparty> counterparties = _context.Counterparties.IgnoreQueryFilters();
        IQueryable<CounterpartyType> counterpartyTypes = _context.CounterpartyTypes;
        if (dryRun)
        {
            counterparties = counterparties.AsNoTracking();
            counterpartyTypes = counterpartyTypes.AsNoTracking();
        }

        List<Counterparty> rows = await counterparties
            .Include(c => c.CounterpartyTypes)
            .AsSplitQuery()
            .Where(c => c.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

        HashSet<string> unmatchedIds = new(bundleIds, StringComparer.OrdinalIgnoreCase);
        unmatchedIds.ExceptWith(rows.Select(r => r.Id));
        List<string> foreignIds = unmatchedIds.Count == 0
            ? []
            : await counterparties
                .Where(c => c.OrganizationId != organizationId && unmatchedIds.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

        List<CounterpartyType> types = await counterpartyTypes.ToListAsync(cancellationToken);

        return new TargetState(rows, foreignIds, types);
    }

    /// <summary>
    /// The organization's counterparties as they stood before the import. Every match reads this
    /// snapshot, so a row this import creates or renames is never a match for a later row.
    /// </summary>
    private sealed class TargetState
    {
        private readonly ILookup<string, Counterparty> _byId;
        private readonly ILookup<string, Counterparty> _liveByName;

        public TargetState(List<Counterparty> rows, List<string> foreignIds, List<CounterpartyType> types)
        {
            ForeignIds = new HashSet<string>(foreignIds, StringComparer.OrdinalIgnoreCase);
            Types = types;
            _byId = rows.ToLookup(r => r.Id, StringComparer.OrdinalIgnoreCase);
            _liveByName = rows.Where(r => !r.IsDeleted).ToLookup(r => r.Name, StringComparer.OrdinalIgnoreCase);
        }

        public HashSet<string> ForeignIds { get; }

        public List<CounterpartyType> Types { get; }

        public Counterparty? FindById(string id)
        {
            IEnumerable<Counterparty> candidates = _byId[id];
            return candidates.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal)) ?? candidates.FirstOrDefault();
        }

        public Counterparty? FindLiveById(string id)
        {
            Counterparty? row = FindById(id);
            return row is { IsDeleted: false } ? row : null;
        }

        public List<Counterparty> FindLiveByName(string name)
        {
            return [.. _liveByName[name]];
        }
    }

    /// <summary>
    /// Counterparty types by name, deployment-wide. A name the deployment lacks is created once,
    /// with the description the bundle carries for it, and reported in the plan.
    /// </summary>
    private sealed class TypeCatalog
    {
        private readonly Dictionary<string, CounterpartyType> _byName;
        private readonly Dictionary<string, string?> _bundledDescriptions;
        private readonly List<CounterpartyType> _created = [];

        public TypeCatalog(List<CounterpartyType> existing, List<CounterpartyBundleTypeDto> bundled)
        {
            _byName = new Dictionary<string, CounterpartyType>(StringComparer.OrdinalIgnoreCase);
            foreach (CounterpartyType type in existing)
            {
                _ = _byName.TryAdd(type.Name, type);
            }

            _bundledDescriptions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (CounterpartyBundleTypeDto type in bundled.Where(t => !string.IsNullOrWhiteSpace(t.Name)))
            {
                _ = _bundledDescriptions.TryAdd(type.Name.Trim(), type.Description);
            }
        }

        public IReadOnlyList<CounterpartyType> Created => _created;

        public List<CounterpartyType> Resolve(IReadOnlyList<string> names)
        {
            var resolved = new List<CounterpartyType>(names.Count);
            foreach (string name in names)
            {
                if (!_byName.TryGetValue(name, out CounterpartyType? type))
                {
                    type = new CounterpartyType(name)
                    {
                        Description = _bundledDescriptions.GetValueOrDefault(name),
                    };
                    _byName[name] = type;
                    _created.Add(type);
                }

                resolved.Add(type);
            }

            return resolved;
        }
    }

    /// <summary>
    /// What the bundle carries, where each of its rows landed, and which target counterparties and
    /// names are taken. The name set is what the pre-import snapshot cannot see: two rows of one
    /// bundle heading for one name.
    /// </summary>
    private sealed class Landing
    {
        private readonly HashSet<string> _bundleIds;
        private readonly Dictionary<string, string> _targetBySource = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _claimed = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

        public Landing(HashSet<string> bundleIds)
        {
            _bundleIds = bundleIds;
        }

        public bool IsBundled(string id)
        {
            return _bundleIds.Contains(id);
        }

        public void Record(string sourceId, string targetId, string name)
        {
            _targetBySource[sourceId] = targetId;
            _ = _claimed.Add(targetId);
            _ = _names.Add(name);
        }

        public bool IsNameTaken(string name)
        {
            return _names.Contains(name);
        }

        public bool TryGetTarget(string sourceId, out string? targetId)
        {
            return _targetBySource.TryGetValue(sourceId, out targetId);
        }

        public bool IsClaimed(string targetId)
        {
            return _claimed.Contains(targetId);
        }
    }
}
