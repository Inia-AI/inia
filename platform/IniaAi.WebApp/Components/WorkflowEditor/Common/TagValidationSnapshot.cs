namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// Per-block tag-validation result published by <c>InvalidReplacementTagsAlert</c> and
/// cascaded to tag-aware inputs: <see cref="InvalidTags"/> drive unknown-tag styling
/// (brace-wrapped, matching the alert verbatim) and <see cref="AvailableTags"/> feed
/// the prompt-editor autocomplete (block tags plus workflow variables, no braces).
/// </summary>
public sealed record TagValidationSnapshot(
    IReadOnlyList<string> InvalidTags,
    IReadOnlyList<string> AvailableTags);
