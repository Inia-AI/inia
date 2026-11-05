using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// The outcome of promoting a template node to (or attaching it to) a Library component.
/// </summary>
/// <param name="SharedComponentId">The component the node is now linked to.</param>
/// <param name="Name">The component's name.</param>
/// <param name="Kind">Whether the component is a block or a section.</param>
/// <param name="ComponentVersionHash">The component version hash stamped on the instance.</param>
/// <param name="WasAttached">True when the node was attached to an existing component instead of creating one.</param>
/// <param name="TargetPath">The canonical path of the linked node.</param>
/// <param name="VersionId">The new template head version id — use as the next <c>baseVersionId</c>.</param>
/// <param name="ContentHash">The template's content hash; a link-only promote leaves it unchanged.</param>
/// <param name="WasDeduped">True when the save was a byte-identical no-op.</param>
public record PromoteSharedComponentResponse(
    string SharedComponentId,
    string Name,
    SharedComponentKind Kind,
    string ComponentVersionHash,
    bool WasAttached,
    string TargetPath,
    string VersionId,
    string ContentHash,
    bool WasDeduped);
