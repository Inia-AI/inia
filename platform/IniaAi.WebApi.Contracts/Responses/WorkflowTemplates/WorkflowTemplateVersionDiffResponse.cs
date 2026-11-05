using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

/// <summary>
/// What differs between two versions of one workflow template, read from
/// <paramref name="FromVersionId"/> to <paramref name="ToVersionId"/>.
/// </summary>
/// <param name="WorkflowTypeId">The workflow type identifier.</param>
/// <param name="FromVersionId">The "before" version, or null when <paramref name="ToVersionId"/> is the first version of the type.</param>
/// <param name="ToVersionId">The "after" version.</param>
/// <param name="IsFirstVersion">True when the version has no predecessor to diff against; <paramref name="Diff"/> is then null.</param>
/// <param name="StoredNarrative">The summary stored on <paramref name="ToVersionId"/> at save time. Only the changes route fills it.</param>
/// <param name="NarrativeParts">One plain-language sentence per changed element, computed from <paramref name="Diff"/>.</param>
/// <param name="Diff">The structured differences, or null for a first version.</param>
public record WorkflowTemplateVersionDiffResponse(
    string WorkflowTypeId,
    string? FromVersionId,
    string ToVersionId,
    bool IsFirstVersion,
    string? StoredNarrative,
    IReadOnlyList<string> NarrativeParts,
    WorkflowTemplateDiffDto? Diff);

/// <summary>
/// Structured differences between two workflow template versions. Blocks match by
/// <c>ReplacementTag</c> across the whole template, variables by replacement tag, document classes
/// by key, and sections by the blocks they hold, so reordering alone reads as no change.
/// </summary>
/// <param name="IsEmpty">True when the two versions have no content differences.</param>
/// <param name="Sections">Added, removed and modified sections.</param>
/// <param name="Blocks">Added, removed, modified and type-changed blocks.</param>
/// <param name="Variables">Added, removed and modified workflow variables.</param>
/// <param name="DocumentClasses">Added, removed and modified document classes.</param>
/// <param name="WorkflowFields">Changed workflow-level fields that live outside the collections above, such as the output title.</param>
public record WorkflowTemplateDiffDto(
    bool IsEmpty,
    IReadOnlyList<WorkflowTemplateSectionDiffDto> Sections,
    IReadOnlyList<WorkflowTemplateBlockDiffDto> Blocks,
    IReadOnlyList<WorkflowTemplateVariableDiffDto> Variables,
    IReadOnlyList<WorkflowTemplateDocumentClassDiffDto> DocumentClasses,
    IReadOnlyList<WorkflowTemplateFieldDiffDto> WorkflowFields);

/// <summary>
/// One changed field. <paramref name="Before"/> and <paramref name="After"/> are the full values,
/// not excerpts.
/// </summary>
/// <param name="FieldName">Dotted path within the element, e.g. <c>Properties.UserPromptTemplate</c>.</param>
/// <param name="Before">The value in the "from" version, or null when the field was absent or null.</param>
/// <param name="After">The value in the "to" version, or null when the field is absent or null.</param>
public record WorkflowTemplateFieldDiffDto(string FieldName, string? Before, string? After);

/// <summary>
/// One changed block. Added and removed blocks carry no field diffs; read the block from the
/// version content instead.
/// </summary>
/// <param name="ReplacementTag">The block's replacement tag, the identity blocks are matched on.</param>
/// <param name="Name">The block's name, falling back to the replacement tag.</param>
/// <param name="BlockType">The block type (the "to" side type when it changed).</param>
/// <param name="StableBlockId">The block's stable id, or null on payloads saved before stable ids existed.</param>
/// <param name="SectionTitle">Title (else name) of the section holding the block: the "to" side, or the "from" side for a removed block.</param>
/// <param name="ChangeKind">How the block changed.</param>
/// <param name="IsLibraryLinked">True when the block is a linked copy of a Library component.</param>
/// <param name="FieldDiffs">The changed fields with full before and after values.</param>
public record WorkflowTemplateBlockDiffDto(
    string ReplacementTag,
    string Name,
    string BlockType,
    string? StableBlockId,
    string SectionTitle,
    WorkflowTemplateChangeKind ChangeKind,
    bool IsLibraryLinked,
    IReadOnlyList<WorkflowTemplateFieldDiffDto> FieldDiffs);

/// <summary>
/// One changed section. Block changes inside it are reported under the diff's blocks.
/// </summary>
/// <param name="Title">The section's title, falling back to its name; may be empty.</param>
/// <param name="Order">The section's order in the version it was read from.</param>
/// <param name="StableSectionId">The section's stable id, or null on payloads saved before stable ids existed.</param>
/// <param name="ChangeKind">How the section changed.</param>
/// <param name="IsLibraryLinked">True when the section is a linked copy of a Library component.</param>
/// <param name="FieldDiffs">The section's own changed fields (its blocks excluded).</param>
public record WorkflowTemplateSectionDiffDto(
    string Title,
    int Order,
    string? StableSectionId,
    WorkflowTemplateChangeKind ChangeKind,
    bool IsLibraryLinked,
    IReadOnlyList<WorkflowTemplateFieldDiffDto> FieldDiffs);

/// <summary>
/// One changed workflow variable.
/// </summary>
/// <param name="ReplacementTag">The variable's replacement tag.</param>
/// <param name="DisplayName">The variable's display name, falling back to the tag.</param>
/// <param name="ChangeKind">How the variable changed.</param>
/// <param name="FieldDiffs">The changed fields of a modified variable.</param>
public record WorkflowTemplateVariableDiffDto(
    string ReplacementTag,
    string DisplayName,
    WorkflowTemplateChangeKind ChangeKind,
    IReadOnlyList<WorkflowTemplateFieldDiffDto> FieldDiffs);

/// <summary>
/// One changed document class.
/// </summary>
/// <param name="Key">The document class key.</param>
/// <param name="DisplayName">The class's display name, falling back to the key.</param>
/// <param name="ChangeKind">How the class changed.</param>
/// <param name="FieldDiffs">The changed fields of a modified class.</param>
public record WorkflowTemplateDocumentClassDiffDto(
    string Key,
    string DisplayName,
    WorkflowTemplateChangeKind ChangeKind,
    IReadOnlyList<WorkflowTemplateFieldDiffDto> FieldDiffs);
