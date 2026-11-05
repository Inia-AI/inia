namespace IniaAi.WebApi.Contracts.Responses.Signatures;

/// <summary>
/// A user who approved the pending whose resolution caused the value change. Captured
/// at promotion time so the History UI can render "signed off by" without joining
/// against the now-deleted pending/vote rows.
/// </summary>
public record SignatureHistoryApproverDto(string UserId, string? Email);

/// <summary>
/// One value transition on a <c>SignatureFieldOverride</c>. <see cref="Action"/> is one
/// of <c>"Created"</c>, <c>"Updated"</c>, <c>"Removed"</c>, or <c>"Renamed"</c>;
/// <see cref="PreviousValue"/> is null on <c>Created</c> and <see cref="NewValue"/> is
/// null on <c>Removed</c>. <see cref="BlockId"/> is the frozen legacy block association —
/// null on rows written after signature fields moved to workflow level; those rows carry
/// <see cref="UsedByBlockNames"/> instead, a snapshot of the block names whose templates
/// referenced the field when the row was written.
/// </summary>
public record SignatureHistoryEntryDto(
    string Id,
    string? BlockId,
    string PlaceholderName,
    string Action,
    string? PreviousValue,
    string? NewValue,
    string? ActorUserId,
    string? ActorUserEmail,
    IReadOnlyList<SignatureHistoryApproverDto> Approvers,
    IReadOnlyList<string>? UsedByBlockNames,
    DateTime OccurredAt);

/// <summary>
/// One page of history entries for a workflow's signature overrides, ordered newest
/// first. <see cref="TotalCount"/> is the unpaged total so the UI can render
/// pagination controls.
/// </summary>
public record SignatureHistoryPageDto(
    IReadOnlyList<SignatureHistoryEntryDto> Items,
    int TotalCount,
    int Skip,
    int Take);
