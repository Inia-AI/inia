using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowTransfer;

/// <summary>
/// The result of applying a transfer bundle: what happened to the template and to each shared
/// component, plus the disclosure of what a bundle never carries.
/// </summary>
/// <param name="WorkflowTypeId">The workflow type the bundle was imported into.</param>
/// <param name="Template">What apply did to the template.</param>
/// <param name="ComponentOutcomes">One outcome per shared component the bundle carried.</param>
/// <param name="SkippedCarriers">Human-readable names of the things a bundle never transfers.</param>
public record WorkflowTransferApplyResponse(
    string WorkflowTypeId,
    TransferTemplateMutation Template,
    IReadOnlyList<TransferComponentOutcome> ComponentOutcomes,
    IReadOnlyList<string> SkippedCarriers);

/// <summary>What apply did to the template.</summary>
/// <param name="VersionId">The template head version id after apply.</param>
/// <param name="ContentHash">The template head content hash after apply.</param>
/// <param name="WasDeduped">True when take-theirs found the bundle template already matched the head.</param>
public sealed record TransferTemplateMutation(
    string? VersionId,
    string? ContentHash,
    bool WasDeduped);

/// <summary>What apply did to one shared component.</summary>
/// <param name="BundleComponentId">The component's id in the bundle.</param>
/// <param name="LocalComponentId">The local component's id after apply.</param>
/// <param name="Action">What apply did — created, appended, deduped, or kept.</param>
/// <param name="HeadVersionId">The component's head version id after apply.</param>
/// <param name="HeadContentHash">The component's head content hash after apply.</param>
public sealed record TransferComponentOutcome(
    string BundleComponentId,
    string? LocalComponentId,
    SharedComponentTransferComponentAction Action,
    string? HeadVersionId,
    string? HeadContentHash);
