using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;

namespace IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;

public record ImportWorkflowTemplateApplyCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    WorkflowTransferBundle Bundle,
    string? BaseVersionId,
    SharedComponentTransferArm TemplateArm,
    bool AcknowledgeUnrelatedHistory,
    bool AcknowledgeSignatureOrphans,
    IReadOnlyList<ImportComponentDecision> ComponentDecisions)
    : IRequest<ErrorOr<ImportWorkflowTemplateApplyResult>>;

public sealed record ImportComponentDecision(
    string ComponentId,
    SharedComponentTransferArm Arm,
    string? ExpectedLocalHeadVersionId);

public sealed record ImportWorkflowTemplateApplyResult(
    string WorkflowTypeId,
    ImportTemplateMutation Template,
    IReadOnlyList<ImportComponentOutcome> ComponentOutcomes,
    IReadOnlyList<string> SkippedCarriers);

public sealed record ImportTemplateMutation(
    string? VersionId,
    string? ContentHash,
    bool WasDeduped);

public sealed record ImportComponentOutcome(
    string BundleComponentId,
    string? LocalComponentId,
    SharedComponentTransferComponentAction Action,
    string? HeadVersionId,
    string? HeadContentHash);
