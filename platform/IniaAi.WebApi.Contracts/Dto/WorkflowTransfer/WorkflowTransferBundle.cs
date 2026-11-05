namespace IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;

/// <summary>
/// A self-contained package of one workflow template plus every shared component it depends on,
/// produced by export on one deployment and consumed by import on another. The bundle carries the
/// template's stored definition verbatim and each component's head definition plus fingerprint
/// trail, so the target can reconcile against whatever it already holds without assuming which side
/// is the original.
/// </summary>
/// <param name="BundleFormatVersion">
/// The bundle envelope's format version. A target that supports a lower version rejects the bundle
/// rather than reading it lossily.
/// </param>
/// <param name="OriginWorkflowTypeId">The workflow type this bundle was exported from.</param>
/// <param name="WorkflowTypeName">
/// The workflow type's display name on the origin deployment, used to create the type on a target
/// that does not have it yet.
/// </param>
/// <param name="WorkflowTypeDescription">The workflow type's description on the origin deployment, when set.</param>
/// <param name="ExportedAtUtc">When the bundle was produced.</param>
/// <param name="ExportedByUserId">The user who produced the bundle.</param>
/// <param name="TemplateJson">The template head version's stored definition payload, verbatim.</param>
/// <param name="TemplateHashTrail">The template's version content hashes, oldest first through the head.</param>
/// <param name="Components">Every shared component the template depends on, at its head.</param>
/// <param name="Categories">The categories referenced by <paramref name="Components"/>, as identity plus display.</param>
public record WorkflowTransferBundle(
    int BundleFormatVersion,
    string OriginWorkflowTypeId,
    string WorkflowTypeName,
    string? WorkflowTypeDescription,
    DateTime ExportedAtUtc,
    string ExportedByUserId,
    string TemplateJson,
    List<string> TemplateHashTrail,
    List<TransferComponentDto> Components,
    List<TransferCategoryDto> Categories);
