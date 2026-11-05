using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowTransfer;

/// <summary>
/// The read-only result of import-preview: how the imported bundle's template and each of its shared
/// components relate to what this deployment already holds, plus the token needed to apply the import
/// and the disclosure of what a bundle never carries.
/// </summary>
/// <param name="WorkflowTypeId">The target workflow type the bundle would be imported into.</param>
/// <param name="OriginWorkflowTypeId">The workflow type the bundle was exported from.</param>
/// <param name="BaseVersionId">
/// The local template head's version id — pass it back as the apply token so the apply is rejected if
/// the head moved in the meantime. Null when the target has no template head.
/// </param>
/// <param name="Template">How the bundle's template relates to the local template head.</param>
/// <param name="Components">One row per shared component the bundle carries.</param>
/// <param name="SkippedCarriers">Human-readable names of the things a bundle never transfers.</param>
public record WorkflowTransferPreviewResponse(
    string WorkflowTypeId,
    string OriginWorkflowTypeId,
    string? BaseVersionId,
    TransferTemplatePreviewRow Template,
    IReadOnlyList<TransferComponentPreviewRow> Components,
    IReadOnlyList<string> SkippedCarriers);

/// <summary>How the bundle's template relates to the local template head.</summary>
/// <param name="Relation">The computed relation between the bundle and local template heads.</param>
/// <param name="ConflictCode">
/// Set when the relation needs the caller's attention on apply — e.g. diverged histories that share
/// no content.
/// </param>
/// <param name="BundleHashTrail">The bundle template's version content hashes, oldest first.</param>
/// <param name="LocalHashTrail">The local template's version content hashes, oldest first.</param>
public sealed record TransferTemplatePreviewRow(
    SharedComponentTransferRelation Relation,
    string? ConflictCode,
    IReadOnlyList<string> BundleHashTrail,
    IReadOnlyList<string> LocalHashTrail);

/// <summary>How one bundle component relates to this deployment's own copy.</summary>
/// <param name="BundleComponentId">The component's id on the origin deployment.</param>
/// <param name="LocalComponentId">The correlated local component's id, or null when it is new here.</param>
/// <param name="Kind">Whether the component is a block or a section.</param>
/// <param name="Name">The component's display name on the origin deployment.</param>
/// <param name="Relation">The computed relation between the bundle and local component heads.</param>
/// <param name="CorrelationMethod">How the bundle component was matched to a local one.</param>
/// <param name="ConflictCode">Set when the relation needs the caller's attention on apply.</param>
/// <param name="BundleHeadContentHash">The content hash of the component's head in the bundle.</param>
/// <param name="LocalHeadContentHash">The content hash of the correlated local head, or null when new.</param>
/// <param name="LocalHeadVersionId">
/// The correlated local head's version id, or null when the component is new here — pass it back as
/// the decision's ExpectedLocalHeadVersionId when applying a take-theirs pick for this component.
/// </param>
/// <param name="BundleHashTrail">The bundle component's version content hashes, oldest first.</param>
/// <param name="LocalHashTrail">The local component's version content hashes, or null when new.</param>
/// <param name="RefWarnings">Advisory strings for a Section-kind row, one per entry reference whose target would not survive the apply as-is; each embeds the code the apply would fail with.</param>
public sealed record TransferComponentPreviewRow(
    string BundleComponentId,
    string? LocalComponentId,
    SharedComponentKind Kind,
    string Name,
    SharedComponentTransferRelation Relation,
    SharedComponentTransferCorrelationMethod CorrelationMethod,
    string? ConflictCode,
    string BundleHeadContentHash,
    string? LocalHeadContentHash,
    string? LocalHeadVersionId,
    IReadOnlyList<string> BundleHashTrail,
    IReadOnlyList<string>? LocalHashTrail,
    IReadOnlyList<string>? RefWarnings = null);
