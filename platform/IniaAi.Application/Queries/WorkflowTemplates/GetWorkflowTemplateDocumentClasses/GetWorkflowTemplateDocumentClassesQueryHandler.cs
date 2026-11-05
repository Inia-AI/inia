using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateDocumentClasses;

public class GetWorkflowTemplateDocumentClassesQueryHandler
    : IRequestHandler<GetWorkflowTemplateDocumentClassesQuery, ErrorOr<DocumentClassListResult>>
{
    private readonly ISender _sender;

    public GetWorkflowTemplateDocumentClassesQueryHandler(ISender sender)
    {
        _sender = sender;
    }

    public async Task<ErrorOr<DocumentClassListResult>> Handle(
        GetWorkflowTemplateDocumentClassesQuery request,
        CancellationToken cancellationToken)
    {
        ErrorOr<WorkflowTemplateContentResult> content = await _sender.Send(
            new GetWorkflowTemplateContentQuery(request.OrganizationId, request.UserId, request.WorkflowTypeId),
            cancellationToken);
        if (content.IsError)
        {
            return content.Errors;
        }

        Workflow? workflow = Workflow.FromJson(content.Value.WorkflowJson);
        if (workflow is null)
        {
            return Error.Unexpected(
                "WorkflowTemplate.Corrupt",
                "The stored workflow template could not be read.");
        }

        List<DocumentClassView> documentClasses = [.. workflow.DocumentClasses.Select(DocumentClassView.From)];

        return new DocumentClassListResult(
            content.Value.WorkflowTypeId,
            content.Value.VersionId,
            documentClasses);
    }
}
