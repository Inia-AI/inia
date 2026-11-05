namespace IniaAi.WebApi.Contracts.Requests.SharedComponents;

/// <summary>
/// Inserts a linked copy of a Library component into the template. The server performs the whole
/// materialization — copy, fresh stable ids, tag-collision rewrite, bindings, link stamping.
/// </summary>
/// <param name="SharedComponentId">The component to materialize.</param>
/// <param name="TargetSectionPath">For block components: the target section's canonical path (<c>Sections[i]</c>). At most one of this and <paramref name="TargetSectionStableId"/>; section components take no section locator.</param>
/// <param name="TargetSectionStableId">For block components: the target section's stable id.</param>
/// <param name="Index">Landing position — block index within the section, or section index within the workflow. Omitted appends.</param>
/// <param name="ReplacementTag">For block components: the instance tag; must be unused in the template. Omitted derives a unique tag from the component name.</param>
/// <param name="BaseVersionId">The template head version this edit is based on; a stale value returns 409 with the current head in the problem-details extensions.</param>
public record MaterializeSharedComponentRequest(
    string SharedComponentId,
    string? TargetSectionPath = null,
    string? TargetSectionStableId = null,
    int? Index = null,
    string? ReplacementTag = null,
    string? BaseVersionId = null);
