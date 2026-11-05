namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// A row that has no foreign key but carries, in <see cref="LooseIdColumns"/>, the id of something the
/// soft-delete sweep purges. No cascade and no foreign-key walk reaches such a row, so this declaration
/// states what the sweep does with it: deletes it (<see cref="RowDeleted"/>), clears
/// <see cref="NulledColumns"/> when the parent that carries the customer content is purged, or, with
/// neither, keeps it whole.
/// </summary>
public sealed record LooseReferenceDisposition(
    Type Entity,
    IReadOnlyList<string> LooseIdColumns,
    IReadOnlyList<string> NulledColumns,
    bool RowDeleted = false);
