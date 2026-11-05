using IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// The outcome of materializing a Library component into a template.
/// </summary>
/// <param name="SharedComponentId">The component that was materialized.</param>
/// <param name="ComponentVersionHash">The component version hash stamped on the landed instance.</param>
/// <param name="LandedPath">The canonical path of the landed node.</param>
/// <param name="LandedReplacementTag">The instance tag assigned to a landed block component.</param>
/// <param name="Bindings">Slot-tag renames applied to avoid collisions (definition tag → instance tag); an absent key means identity. Null when nothing was renamed.</param>
/// <param name="VersionId">The new template head version id — use as the next <c>baseVersionId</c>.</param>
/// <param name="ContentHash">The template's content hash after the insert.</param>
/// <param name="WasDeduped">True when the save was a byte-identical no-op.</param>
/// <param name="Warnings">Non-blocking structural issues found in the template after the insert — e.g. definition references to tags the template lacks.</param>
public record MaterializeSharedComponentResponse(
    string SharedComponentId,
    string ComponentVersionHash,
    string LandedPath,
    string? LandedReplacementTag,
    Dictionary<string, string>? Bindings,
    string VersionId,
    string ContentHash,
    bool WasDeduped,
    List<WorkflowTemplateWarningDto> Warnings);
