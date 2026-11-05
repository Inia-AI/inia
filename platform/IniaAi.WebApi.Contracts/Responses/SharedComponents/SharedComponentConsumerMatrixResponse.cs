using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// The consumers×entries grid for a section component: one row per live linked copy, one column per
/// definition entry plus any slot a consumer inserted locally.
/// </summary>
/// <param name="SharedComponentId">The component the grid describes.</param>
/// <param name="HeadVersionId">The head version the columns were read from.</param>
/// <param name="HeadContentHash">The head version's content hash.</param>
/// <param name="Entries">The columns, definition entries first in definition order.</param>
/// <param name="Rows">The consuming copies, ordered by workflow type name. Empty for a block component.</param>
public record SharedComponentConsumerMatrixResponse(
    string SharedComponentId,
    string HeadVersionId,
    string HeadContentHash,
    List<SharedComponentMatrixEntryDto> Entries,
    List<SharedComponentConsumerMatrixRowDto> Rows);

/// <summary>
/// One column of the grid.
/// </summary>
/// <param name="SlotTag">The entry's slot tag, which is also the replacement tag of the block filling it.</param>
/// <param name="Kind">Whether the definition fills the entry inline or by referencing a Block component.</param>
/// <param name="RefSharedComponentId">For a <see cref="SectionEntryKind.ComponentRef"/> entry, the referenced component.</param>
/// <param name="IsLocalOnly">True when no definition entry owns this slot — the column exists only because a consumer inserted a block there.</param>
public record SharedComponentMatrixEntryDto(
    string SlotTag,
    SectionEntryKind Kind,
    string? RefSharedComponentId,
    bool IsLocalOnly);

/// <summary>
/// One consuming copy's row.
/// </summary>
/// <param name="WorkflowTypeId">The workflow type holding the copy.</param>
/// <param name="WorkflowTypeName">Its display name, falling back to the workflow name then the identifier.</param>
/// <param name="StableSectionId">The copy's stable section id, which distinguishes two copies inside one workflow type.</param>
/// <param name="Cells">One cell per column, in the same order as <c>Entries</c>.</param>
public record SharedComponentConsumerMatrixRowDto(
    string WorkflowTypeId,
    string WorkflowTypeName,
    string? StableSectionId,
    List<SharedComponentConsumerMatrixCellDto> Cells);

/// <summary>
/// One cell of the grid.
/// </summary>
/// <param name="SlotTag">The column this cell belongs to.</param>
/// <param name="State">Where this copy stands on that entry.</param>
public record SharedComponentConsumerMatrixCellDto(
    string SlotTag,
    SharedComponentEntryCellState State);
