using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;

namespace IniaAi.Application.Queries.WorkflowTransfer.ExportWorkflowTemplate;

public record ExportWorkflowTemplateQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId) : IRequest<ErrorOr<ExportWorkflowTemplateResult>>;

public sealed record ExportWorkflowTemplateResult(
    WorkflowTransferBundle Bundle,
    IReadOnlyList<string> SkippedCarriers);
