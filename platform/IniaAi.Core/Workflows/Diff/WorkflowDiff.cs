using IniaAi.Domain.Entities.SharedComponents.Enums;

namespace IniaAi.Core.Workflows.Diff;

public static class SharedComponentDiffFields
{
    public const string PREFIX = "SharedComponent";
    public const string ID = "SharedComponentId";
    public const string LINK_STATE = "SharedComponentLinkState";
    public const string VERSION_HASH = "SharedComponentVersionHash";
    public const string LINKED = nameof(SharedComponentLinkState.Linked);
    public const string BROKEN_OUT = nameof(SharedComponentLinkState.BrokenOut);
}

public enum DiffChangeKind
{
    Added,
    Removed,
    Modified,
    TypeChanged,
}

/// <param name="FieldName">Dotted path within the block, e.g. <c>Properties.UserPromptTemplate</c>.</param>
public record FieldDiff(string FieldName, string? Before, string? After);

public record BlockDiff(
    string ReplacementTag,
    string DisplayName,
    string BlockType,
    DiffChangeKind ChangeKind,
    IReadOnlyList<FieldDiff> FieldDiffs)
{
    public bool IsLibraryLinked { get; init; }

    /// <summary>Title (else name) of the section holding the block: the after side, or the before side for a removed block.</summary>
    public string SectionName { get; init; } = string.Empty;

    /// <summary>Null on payloads saved before stable ids rode in the payload.</summary>
    public string? StableBlockId { get; init; }
}

public record SectionDiff(
    string Name,
    int Order,
    DiffChangeKind ChangeKind)
{
    public bool IsLibraryLinked { get; init; }

    public IReadOnlyList<FieldDiff> FieldDiffs { get; init; } = [];

    /// <summary>Null on payloads saved before stable ids rode in the payload.</summary>
    public string? StableSectionId { get; init; }
}

public record VariableDiff(
    string ReplacementTag,
    string DisplayName,
    DiffChangeKind ChangeKind)
{
    public IReadOnlyList<FieldDiff> FieldDiffs { get; init; } = [];
}

public record DocumentClassDiff(
    string Key,
    string DisplayName,
    DiffChangeKind ChangeKind)
{
    public IReadOnlyList<FieldDiff> FieldDiffs { get; init; } = [];
}

public record WorkflowDiff(
    IReadOnlyList<SectionDiff> Sections,
    IReadOnlyList<BlockDiff> Blocks,
    IReadOnlyList<VariableDiff> Variables,
    IReadOnlyList<DocumentClassDiff> DocumentClasses)
{
    /// <summary>
    /// Changes to workflow-level fields that live outside sections, blocks, variables and document
    /// classes — e.g. the output title. Without this, editing only such a field changes the saved
    /// content but produces an empty, summary-less diff.
    /// </summary>
    public IReadOnlyList<FieldDiff> WorkflowFields { get; init; } = [];

    public bool IsEmpty =>
        Sections.Count == 0
        && Blocks.Count == 0
        && Variables.Count == 0
        && DocumentClasses.Count == 0
        && WorkflowFields.Count == 0;
}
