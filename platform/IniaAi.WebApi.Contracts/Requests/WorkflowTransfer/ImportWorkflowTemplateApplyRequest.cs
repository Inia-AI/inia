using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.WorkflowTransfer;

/// <summary>
/// Applies a transfer bundle to a workflow type, resolving each element the preview flagged with the
/// caller's take-theirs / keep-mine choices.
/// </summary>
/// <param name="Bundle">The same bundle that was previewed.</param>
/// <param name="BaseVersionId">
/// The template head version id from the preview — the apply is rejected if the local head moved
/// since.
/// </param>
/// <param name="TemplateArm">Whether to adopt the bundle's template or keep the local one.</param>
/// <param name="AcknowledgeUnrelatedHistory">
/// Set true to confirm applying a take-theirs whose history shares nothing with the local copy.
/// </param>
/// <param name="AcknowledgeSignatureOrphans">
/// Set true to confirm a template change that orphans signature field definitions.
/// </param>
/// <param name="ComponentDecisions">One decision per shared component the bundle carries.</param>
public record ImportWorkflowTemplateApplyRequest(
    WorkflowTransferBundle Bundle,
    string? BaseVersionId,
    SharedComponentTransferArm TemplateArm,
    bool AcknowledgeUnrelatedHistory,
    bool AcknowledgeSignatureOrphans,
    List<ImportComponentDecisionDto> ComponentDecisions);

/// <summary>The caller's resolution for one shared component in the bundle.</summary>
/// <param name="ComponentId">The component's id in the bundle (its origin-deployment id).</param>
/// <param name="Arm">Whether to adopt the bundle's version or keep the local one.</param>
/// <param name="ExpectedLocalHeadVersionId">
/// The correlated local head version id from the preview — required for a correlated take-theirs, and
/// rejected if the local head moved since.
/// </param>
public sealed record ImportComponentDecisionDto(
    string ComponentId,
    SharedComponentTransferArm Arm,
    string? ExpectedLocalHeadVersionId);
