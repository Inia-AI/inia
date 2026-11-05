namespace IniaAi.WebApi.Contracts.Requests.SharedComponents;

/// <summary>
/// Turns an existing template block or section into a Library component (or attaches it to an
/// existing content-identical component) and links the instance in place.
/// </summary>
/// <param name="TargetPath">Canonical node path of the target — <c>Sections[i]</c> or <c>Sections[i].Blocks[j]</c>. Exactly one of this and <paramref name="TargetStableId"/>.</param>
/// <param name="TargetStableId">Stable id of the target block or section. Exactly one of this and <paramref name="TargetPath"/>.</param>
/// <param name="Name">Name for the new component; defaults to the node's display name. Not allowed with <paramref name="AttachToComponentId"/>.</param>
/// <param name="Description">Optional description for the new component. Not allowed with <paramref name="AttachToComponentId"/>.</param>
/// <param name="AttachToComponentId">Attach the node to this existing component instead of creating one; its head content hash must match the node's normalized content exactly.</param>
/// <param name="BaseVersionId">The template head version this edit is based on; a stale value returns 409 with the current head in the problem-details extensions.</param>
/// <param name="CategoryId">The curated category the new component files under. Required when creating; list <c>GET /shared-components/categories</c> for the vocabulary. Not allowed with <paramref name="AttachToComponentId"/> — the existing component keeps its category.</param>
/// <param name="ExpectedContainerHeadVersionId">Required exactly when the target block sits inside a Linked section: the current head version of that section component. Promoting rewrites the section's slot inline→reference and fans it out; a stale or missing value returns 409 with the section component's head in the extensions.</param>
public record PromoteSharedComponentRequest(
    string? TargetPath = null,
    string? TargetStableId = null,
    string? Name = null,
    string? Description = null,
    string? AttachToComponentId = null,
    string? BaseVersionId = null,
    string? CategoryId = null,
    string? ExpectedContainerHeadVersionId = null);
