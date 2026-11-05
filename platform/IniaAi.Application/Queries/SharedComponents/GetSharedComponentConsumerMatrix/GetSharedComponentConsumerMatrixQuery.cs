using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Specifications.SharedComponents;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.SharedComponents.GetSharedComponentConsumerMatrix;

/// <summary>
/// The consumers×entries grid behind the Library detail surface: one row per live Linked copy of a
/// section component, one column per entry of its head definition plus any slot a consumer inserted
/// locally.
/// </summary>
/// <remarks>
/// Cell states are field reads, never content comparisons. A persisted Linked copy is always at the
/// component's current version, so a linked entry cannot have drifted; deviation is representable
/// only as a slot state (<c>Hidden</c>/<c>BrokenOut</c>) or an absent block. That is why this query
/// loads no consumer workflow graph and hashes nothing — unlike the reconcile read, which walks the
/// opposite direction (one workflow to its links) and must compare content.
/// </remarks>
public record GetSharedComponentConsumerMatrixQuery(
    string OrganizationId,
    string UserId,
    string SharedComponentId) : IRequest<ErrorOr<SharedComponentConsumerMatrix>>;

public enum SharedComponentEntryCellState
{
    Live = 1,
    Custom = 2,
    Hidden = 3,
    Added = 4,
    Missing = 5,
    NotApplicable = 6,
}

public record SharedComponentConsumerMatrix(
    string SharedComponentId,
    string HeadVersionId,
    string HeadContentHash,
    IReadOnlyList<SharedComponentMatrixEntry> Entries,
    IReadOnlyList<SharedComponentConsumerMatrixRow> Rows);

/// <param name="IsLocalOnly">True when no head-definition entry owns this slot — a column that exists only because some consumer inserted a block there.</param>
public record SharedComponentMatrixEntry(
    string SlotTag,
    SectionEntryKind Kind,
    string? RefSharedComponentId,
    bool IsLocalOnly);

public record SharedComponentConsumerMatrixRow(
    string WorkflowTypeId,
    string WorkflowTypeName,
    string? StableSectionId,
    IReadOnlyList<SharedComponentConsumerMatrixCell> Cells);

public record SharedComponentConsumerMatrixCell(string SlotTag, SharedComponentEntryCellState State);

public class GetSharedComponentConsumerMatrixQueryHandler
    : IRequestHandler<GetSharedComponentConsumerMatrixQuery, ErrorOr<SharedComponentConsumerMatrix>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public GetSharedComponentConsumerMatrixQueryHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<ErrorOr<SharedComponentConsumerMatrix>> Handle(
        GetSharedComponentConsumerMatrixQuery request,
        CancellationToken cancellationToken)
    {
        SharedComponent? component = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetFirstOrDefaultAsync(new SharedComponentByIdSpec(request.SharedComponentId), cancellationToken);
        if (component is null)
        {
            return SharedComponentErrorCodes.UnknownComponent(request.SharedComponentId);
        }

        if (component.Kind != SharedComponentKind.Section)
        {
            return Error.Validation(
                "sharedComponentId",
                $"Shared component '{component.Id}' is a {component.Kind}; the consumers-by-entries grid " +
                "exists only for section components, whose definitions have entries. Read a block " +
                $"component's where-used list from the consumers endpoint instead. " +
                $"[{SharedComponentErrorCodes.KIND_MISMATCH}]");
        }

        SharedComponentVersion? head = await _coreUnitOfWork.Repository<SharedComponentVersion>()
            .GetFirstOrDefaultAsync(new LatestSharedComponentVersionSpec(component.Id), cancellationToken);
        if (head is null)
        {
            return Error.Conflict(
                SharedComponentErrorCodes.NO_VERSIONS,
                $"Shared component '{component.Id}' has no versions at all; its chain is corrupt.");
        }

        if (head.PayloadAbsentAt is not null)
        {
            return Error.NotFound(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"Shared component '{component.Id}' has no stored head content, so its entries cannot be listed.");
        }

        Section? definition = Section.FromJson(head.ContentJson);
        if (definition is null)
        {
            return Error.Failure(
                SharedComponentErrorCodes.CORRUPT_DEFINITION,
                $"The stored head definition of shared component '{component.Id}' could not be read as a section.");
        }

        List<SharedComponentMatrixEntry> entries =
        [
            .. (definition.Entries ?? []).Select(entry => new SharedComponentMatrixEntry(
                entry.SlotTag,
                entry.Kind,
                entry.SharedComponentId,
                IsLocalOnly: false)),
        ];

        List<ConsumerSection> consumers = await LoadConsumerSectionsAsync(component.Id, cancellationToken);
        AppendLocalOnlyColumns(entries, consumers);

        List<SharedComponentConsumerMatrixRow> rows =
        [
            .. consumers
                .Select(consumer => BuildRow(consumer, entries))
                .OrderBy(row => row.WorkflowTypeName, StringComparer.Ordinal)
                .ThenBy(row => row.WorkflowTypeId, StringComparer.Ordinal)
                .ThenBy(row => row.StableSectionId, StringComparer.Ordinal),
        ];

        return new SharedComponentConsumerMatrix(component.Id, head.Id, head.ContentHash, entries, rows);
    }

    private async Task<List<ConsumerSection>> LoadConsumerSectionsAsync(
        string componentId,
        CancellationToken cancellationToken)
    {
        List<ConsumerSection> consumers = await _coreUnitOfWork.Repository<Section>()
            .GetAll()
            .AsNoTracking()
            .Where(section => section.SharedComponentId == componentId
                && section.SharedComponentLinkState == SharedComponentLinkState.Linked)
            .Select(section => new ConsumerSection(
                section.Workflow.WorkflowTypeId,
                section.Workflow.WorkflowType == null ? null : section.Workflow.WorkflowType.Name,
                section.Workflow.Name,
                section.StableSectionId,
                section.SharedComponentSlotStates,
                section.SharedComponentBindings,
                section.Blocks
                    .Select(block => new ConsumerSlot(block.ReplacementTag, block.SharedComponentLinkState))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return consumers;
    }

    private static void AppendLocalOnlyColumns(
        List<SharedComponentMatrixEntry> entries,
        List<ConsumerSection> consumers)
    {
        HashSet<string> known = new(entries.Select(entry => entry.SlotTag), StringComparer.Ordinal);
        foreach (ConsumerSection consumer in consumers)
        {
            HashSet<string> boundLiveTags = BoundLiveTags(consumer);
            foreach (ConsumerSlot slot in consumer.Slots)
            {
                if (string.IsNullOrEmpty(slot.SlotTag)
                    || boundLiveTags.Contains(slot.SlotTag)
                    || !known.Add(slot.SlotTag))
                {
                    continue;
                }

                entries.Add(new SharedComponentMatrixEntry(
                    slot.SlotTag,
                    SectionEntryKind.Inline,
                    RefSharedComponentId: null,
                    IsLocalOnly: true));
            }
        }
    }

    private static SharedComponentConsumerMatrixRow BuildRow(
        ConsumerSection consumer,
        List<SharedComponentMatrixEntry> entries)
    {
        Dictionary<string, ConsumerSlot> slotsByTag = consumer.Slots
            .Where(slot => !string.IsNullOrEmpty(slot.SlotTag))
            .DistinctBy(slot => slot.SlotTag!, StringComparer.Ordinal)
            .ToDictionary(slot => slot.SlotTag!, StringComparer.Ordinal);
        HashSet<string> boundLiveTags = BoundLiveTags(consumer);

        List<SharedComponentConsumerMatrixCell> cells =
        [
            .. entries.Select(entry => new SharedComponentConsumerMatrixCell(
                entry.SlotTag,
                CellState(entry, slotsByTag, consumer.SlotStates, consumer.Bindings, boundLiveTags))),
        ];

        return new SharedComponentConsumerMatrixRow(
            consumer.WorkflowTypeId ?? string.Empty,
            string.IsNullOrWhiteSpace(consumer.WorkflowTypeName)
                ? consumer.WorkflowName ?? consumer.WorkflowTypeId ?? string.Empty
                : consumer.WorkflowTypeName,
            consumer.StableSectionId,
            cells);
    }

    private static SharedComponentEntryCellState CellState(
        SharedComponentMatrixEntry entry,
        IReadOnlyDictionary<string, ConsumerSlot> slotsByTag,
        IReadOnlyDictionary<string, SharedComponentSlotState>? slotStates,
        IReadOnlyDictionary<string, string>? bindings,
        IReadOnlySet<string> boundLiveTags)
    {
        SharedComponentSlotState? slotState = slotStates?.GetValueOrDefault(entry.SlotTag);
        if (slotState == SharedComponentSlotState.Hidden)
        {
            return SharedComponentEntryCellState.Hidden;
        }

        string liveTag = entry.IsLocalOnly
            ? entry.SlotTag
            : bindings?.GetValueOrDefault(entry.SlotTag) ?? entry.SlotTag;
        bool present = slotsByTag.TryGetValue(liveTag, out ConsumerSlot? slot)
            && (!entry.IsLocalOnly || !boundLiveTags.Contains(liveTag));
        if (slotState == SharedComponentSlotState.BrokenOut
            || (present && slot!.LinkState == SharedComponentLinkState.BrokenOut))
        {
            return SharedComponentEntryCellState.Custom;
        }

        if (!present)
        {
            return entry.IsLocalOnly
                ? SharedComponentEntryCellState.NotApplicable
                : SharedComponentEntryCellState.Missing;
        }

        return entry.IsLocalOnly
            ? SharedComponentEntryCellState.Added
            : SharedComponentEntryCellState.Live;
    }

    private static HashSet<string> BoundLiveTags(ConsumerSection consumer)
    {
        return consumer.Bindings is { Count: > 0 }
            ? new HashSet<string>(consumer.Bindings.Values, StringComparer.Ordinal)
            : [];
    }

    private sealed record ConsumerSection(
        string? WorkflowTypeId,
        string? WorkflowTypeName,
        string? WorkflowName,
        string? StableSectionId,
        Dictionary<string, SharedComponentSlotState>? SlotStates,
        Dictionary<string, string>? Bindings,
        List<ConsumerSlot> Slots);

    private sealed record ConsumerSlot(string? SlotTag, SharedComponentLinkState? LinkState);
}

public class GetSharedComponentConsumerMatrixQueryValidator
    : AbstractValidator<GetSharedComponentConsumerMatrixQuery>
{
    public GetSharedComponentConsumerMatrixQueryValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.SharedComponentId).NotEmpty();
    }
}
