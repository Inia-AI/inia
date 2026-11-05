using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;

namespace IniaAi.Application.Queries.WorkflowTransfer.ImportWorkflowTemplatePreview;

public record ImportWorkflowTemplatePreviewQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    WorkflowTransferBundle Bundle) : IRequest<ErrorOr<ImportWorkflowTemplatePreviewResult>>;

public sealed record ImportWorkflowTemplatePreviewResult(
    string WorkflowTypeId,
    string OriginWorkflowTypeId,
    string? BaseVersionId,
    ImportTemplatePreviewRow Template,
    IReadOnlyList<ImportComponentPreviewRow> Components,
    IReadOnlyList<string> SkippedCarriers);

public sealed record ImportTemplatePreviewRow(
    SharedComponentTransferRelation Relation,
    string? ConflictCode,
    IReadOnlyList<string> BundleHashTrail,
    IReadOnlyList<string> LocalHashTrail);

public sealed record ImportComponentPreviewRow(
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
